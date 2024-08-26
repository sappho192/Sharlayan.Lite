# Sharlayan.Lite

This fork is a lightweight version of the original library and only has memory search & ChatLog functionality.

# Nuget Package list

| Package       | repo                                                                                                                            | description  |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| Sharlayan.Lite | [![Nuget Sharlayan.Lite](https://img.shields.io/nuget/v/Sharlayan.Lite.svg?style=flat)](https://www.nuget.org/packages/Sharlayan.Lite/) | Main library |

# How do I use it and what comes back?

- .NET CLI: `dotnet add package Sharlayan.Lite --version 8.0.1`
- Nuget Package Manager: `Install-Package Sharlayan.Lite -Version 8.0.1`
- PackageReference: `<PackageReference Include="Sharlayan.Lite" Version="8.0.1" />`

That's the basic of it. For actual instantiation it works as follows:

```csharp
using Sharlayan;
using Sharlayan.Enums;
using Sharlayan.Models;

// DX11
Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
if (processes.Length > 0)
{
    // supported: Global, Chinese, Korean
    GameRegion gameRegion = GameRegion.Global;
    GameLanguage gameLanguage = GameLanguage.English;
	// whether to always hit API on start to get the latest sigs based on patchVersion, or use the local json cache (if the file doesn't exist, API will be hit)
	bool useLocalCache = true;
	// patchVersion of game, or latest
	string patchVersion = "latest";
    Process process = processes[0];
    ProcessModel processModel = new ProcessModel {
        Process = process
    };
    SharlayanConfiguration configuration = new SharlayanConfiguration {
        ProcessModel = processModel,
        GameLanguage = gameLanguage,
        GameRegion = gameRegion,
        PatchVersion = patchVersion,
        UseLocalCache = useLocalCache
    };
    MemoryHandler memoryHandler = SharlayanMemoryManager.Instance.AddHandler(configuration);
}
```

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

# Roll your own app?

If you want to add your own signatures to scan for you can modify the json file directly that's automatically downloaded/saved into your application directory.

You can also override the built in like so:

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
