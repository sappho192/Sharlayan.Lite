using Sharlayan;
using Sharlayan.Enums;
using Sharlayan.Models;

using System.Diagnostics;

// DX11
Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
if (processes.Length > 0) {
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

    Console.WriteLine("Sharlayan has been initialized.");
} else {
    Console.WriteLine("Failed to find the process.");
}
