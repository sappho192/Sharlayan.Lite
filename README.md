# Sharlayan.Lite

This fork is a lightweight, independently maintained version of the original library with memory search, ChatLog, and dialogue UI readers.

Supported targets: .NET Framework 4.6.2 and 4.8, plus .NET 6, 7, 8, and 10.

## Project status and upstream compatibility

Sharlayan.Lite no longer tracks or ports implementation changes from the original
[`FFXIVAPP/sharlayan`](https://github.com/FFXIVAPP/sharlayan) repository. It has deliberately
diverged around a smaller feature set and its own resource and dialogue-reading implementations.

Do not treat Sharlayan.Lite as a drop-in replacement for a current version of the original
Sharlayan package. Compatibility with the latest upstream public API, behavior, data models, or
implementation is not a project goal and is not guaranteed. Consumers should assume the two
libraries are incompatible unless the exact APIs and behavior they rely on have been verified.
Their package version numbers are independent and do not identify equivalent releases.

# Nuget Package list

| Package       | repo                                                                                                                            | description  |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| Sharlayan.Lite | [![Nuget Sharlayan.Lite](https://img.shields.io/nuget/v/Sharlayan.Lite.svg?style=flat)](https://www.nuget.org/packages/Sharlayan.Lite/) | Main library |

# How do I use it and what comes back?

- .NET CLI: `dotnet add package Sharlayan.Lite --version 9.2.1`
- Nuget Package Manager: `Install-Package Sharlayan.Lite -Version 9.2.1`
- PackageReference: `<PackageReference Include="Sharlayan.Lite" Version="9.2.1" />`

That's the basic of it. For actual instantiation it works as follows:

```csharp
using Sharlayan;
using Sharlayan.Enums;
using Sharlayan.Models;
using Sharlayan.Models.Resources;

// DX11
Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
if (processes.Length > 0)
{
    GameLanguage gameLanguage = GameLanguage.English;
    Process process = processes[0];
    ProcessModel processModel = new ProcessModel {
        Process = process
    };
    SharlayanConfiguration configuration = new SharlayanConfiguration {
        ProcessModel = processModel,
        GameLanguage = gameLanguage,
        ResourceMode = ResourceMode.RemotePreferred,
        ResourceCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyApp")
    };
    MemoryHandler memoryHandler = SharlayanMemoryManager.Instance.AddHandler(configuration);
}
```

`ResourceMode` defaults to `EmbeddedOnly`, so existing consumers do not start network requests merely by
updating the package. `RemotePreferred` validates Hermes v2 remote bytes first, then falls back to the last
valid cache and finally the package's embedded manifest. Remote and cached manifests may use the
`generated` status for CI-published resources or the legacy `live-verified` status. Initialization
uses one manifest revision for both the CHATLOG signature/structure and standard Talk offsets.
The default latest pointer is `https://hermes.sapphosound.com/v2/latest.json`, under the Hermes v2 public
base `https://hermes.sapphosound.com/v2/`.

The memory module is now instantiated and is ready to read data. When switch processes you should call:

```csharp
SharlayanMemoryManager.Instance.RemoveHandler(processModel);
```

# Reading

The instantiated memory handler for the process comes with it's own reader, or you can create your own.

### Using Predefined

```csharp
MemoryHandler memoryHandler = SharlayanMemoryManager.Instance.GetHandler(processModel);
XResult result = memoryHandler.Reader.GetX();
```

## ChatLog Reading

```csharp
using Sharlayan;

// For chatlog you must locally store previous array offsets and indexes in order to pull the correct log from the last time you read it.
int _previousArrayIndex = 0;
int _previousOffset = 0;

var readResult = memoryHandler.Reader.GetChatLog(_previousArrayIndex, _previousOffset);

var chatLogItems = readResult.ChatLogItems;

_previousArrayIndex = readResult.PreviousArrayIndex;
_previousOffset = readResult.PreviousOffset;

// The result is the following class
public class ChatLogResult
{
    public ChatLogResult()
    {
        ChatLogItems = new ConcurrentQueue<ChatLogItem>();
    }

    public ConcurrentQueue<ChatLogItem> ChatLogItems { get; set; }
    public int PreviousArrayIndex { get; set; }
    public int PreviousOffset { get; set; }
}
```

## Standard NPC Talk Reading

```csharp
if (memoryHandler.Reader.CanGetTalk()) {
    TalkResult talk = memoryHandler.Reader.GetTalk();
    if (talk.IsAvailable) {
        Console.WriteLine($"{talk.Source} (visible={talk.IsVisible}): {talk.Name}: {talk.Text}");
    }
}
```

`GetTalk()` returns the visible standard `Talk` addon first (`Source=Current`, `IsVisible=true`) and falls
back to FCS `LastTalkName`/`LastTalkText` (`Source=Last`, `IsVisible=false`) when no stable current Talk is
available. Use `GetCurrentTalk()` or `GetLastTalk()` when the distinction is part of the caller's policy.
Applications consuming the last value should baseline the first value after attach before treating changes
as new dialogue.

## BattleTalk Reading

```csharp
if (memoryHandler.Reader.CanGetBattleTalk()) {
    BattleTalkResult battleTalk = memoryHandler.Reader.GetBattleTalk();
    if (battleTalk.IsAvailable && battleTalk.IsVisible) {
        Console.WriteLine($"{battleTalk.Sequence}: {battleTalk.Name}: {battleTalk.Text}");
    }
}
```

The optional capability returns `false` when the selected Hermes manifest does not contain its
resource. `BattleTalkResult.Sequence` increases for each stable visible generation observed during the
handler lifetime, including the same name/text pair after a stable hidden state.

The selected resource can be inspected through `memoryHandler.ResourceInfo`, including its source,
revision, FCS/generator commits, validation status, resolved location count, and fallback reason.

# Roll your own app?

If you want to scan application-specific signatures, register them explicitly after the built-in Hermes v2
initialization. Do not modify a verified Hermes cache file.

```csharp
SharlayanConfiguration configuration = new SharlayanConfiguration {
    ProcessModel = new ProcessModel {
        Process = Process.GetProcessesByName("ffxiv_dx11").FirstOrDefault(),
    },
};
MemoryHandler memoryHandler = new MemoryHandler(configuration);
// it be default will pull in the memory signatures from the local file, backup from API (GitHub)
memoryHandler.Scanner.Locations.Clear(); // these are resolved MemoryLocation

var signatures = new List<Signature>();
// typical signature
signatures.Add(new Signature
{
	Key = "SOMETHING",
	Value = "0123456789ABCDEF",
	Offset = 0
});
// pointer path based (no signature)
signatures.Add(new Signature
{
	Key = "SOMETHING2",
	PointerPath = new List<long>
	{
		0x0123456789
	}
});
// Aseembly Signature Based
signatures.Add(new Signature
{
	Key = "SOMETING3",
	Value = "0123456789ABCDEF0123456789ABCDEF",
	ASMSignature = true,
	PointerPath = new List<long>
	{
		0L, // ASM assumes first pointer is always 0
		144L
	}
});

memoryHandler.Scanner.LoadOffsets(signatures);
```

Once this is complete you can reference this when reading like so:

```csharp
var somethingMap = memoryHandler.GetByteArray(memoryHandler.Scanner.Locations["SOMETHING"], 8);
```
