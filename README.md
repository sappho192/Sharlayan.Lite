# Sharlayan.Lite

This fork is a lightweight version of the original library and only has memory search & ChatLog functionality.

# Nuget Package list

| Package       | repo                                                                                                                            | description  |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| Sharlayan.Lite | [![Nuget Sharlayan.Lite](https://img.shields.io/nuget/v/Sharlayan.Lite.svg?style=flat)](https://www.nuget.org/packages/Sharlayan.Lite/) | Main library |

# How do I use it and what comes back?

- .NET CLI: `dotnet add package Sharlayan.Lite --version 9.1.2`
- Nuget Package Manager: `Install-Package Sharlayan.Lite -Version 9.1.2`
- PackageReference: `<PackageReference Include="Sharlayan.Lite" Version="9.1.2" />`

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
verified cache and finally the package's embedded manifest. Initialization uses one manifest revision for
both the CHATLOG signature/structure and standard Talk offsets.
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
if (memoryHandler.Reader.CanGetLastTalk()) {
    TalkResult talk = memoryHandler.Reader.GetLastTalk();
    if (talk.IsAvailable) {
        Console.WriteLine($"{talk.Name}: {talk.Text}");
    }
}
```

The result represents FCS `LastTalkName` and `LastTalkText`. It does not claim that the Talk window is
currently open. Applications should baseline the first value after attach before treating changes as new
dialogue.

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
