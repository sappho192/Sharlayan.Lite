namespace Sharlayan.LiveSmoke;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json;

using Sharlayan;
using Sharlayan.Models;
using Sharlayan.Models.ReadResults;
using Sharlayan.Models.Resources;
using Sharlayan.Resources;

internal static class Program {
    private const int BufferSize = 0x1200;

    private const string ConfiguredCharacterName = "LIVE_SMOKE";

    private const int RegionIncrement = 0x1000;

    public static async Task<int> Main(string[] args) {
        try {
            Options options = Options.Parse(args);
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) {
                throw new PlatformNotSupportedException("Live smoke requires a 64-bit Windows process.");
            }

            using Process process = FindProcess(options.ProcessId);
            Console.WriteLine($"Process: {process.ProcessName} ({process.Id})");

            SharlayanConfiguration configuration = new SharlayanConfiguration {
                CharacterName = ConfiguredCharacterName,
                ProcessModel = new ProcessModel { Process = process },
                ResourceMode = options.RemotePreferred ? ResourceMode.RemotePreferred : ResourceMode.EmbeddedOnly,
                ResourceCacheDirectory = options.ResourceCacheDirectory,
            };
            if (options.HermesV2LatestUri != null) {
                configuration.HermesV2LatestUri = options.HermesV2LatestUri;
            }
            Signature signature;
            if (!string.IsNullOrEmpty(options.ManifestPath)) {
                byte[] manifestBytes = File.ReadAllBytes(options.ManifestPath);
                HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(
                    manifestBytes,
                    null,
                    HermesV2ResourceProvider.GetClientVersion(),
                    allowCandidate: true);
                configuration.HermesV2ManifestOverride = manifestBytes;
                signature = HermesV2ResourceMapper.Map(manifest).Signatures.Single(item => item.Key == Signatures.CHATLOG_KEY);
                Console.WriteLine($"Manifest: {options.ManifestPath}, revision={HermesV2ManifestParser.CalculateRevision(manifestBytes)}");
            }
            else {
                signature = (await Signatures.Resolve(configuration)).Single(item => item.Key == Signatures.CHATLOG_KEY);
            }
            (int signatureMatches, int failedReads) = CountSignatureMatches(process, signature.Value);
            Console.WriteLine($"CHATLOG signature matches: {signatureMatches}, failed reads: {failedReads}");
            if (failedReads != 0) {
                throw new InvalidOperationException($"Could not establish exact signature uniqueness because {failedReads} module reads failed.");
            }

            if (signatureMatches != 1) {
                throw new InvalidOperationException($"Expected exactly one CHATLOG signature match, found {signatureMatches}.");
            }

            MemoryHandler? handler = null;
            try {
                handler = SharlayanMemoryManager.Instance.AddHandler(configuration);
                ConcurrentQueue<Exception> exceptions = new ConcurrentQueue<Exception>();
                handler.OnException += (_, _, exception) => exceptions.Enqueue(exception);

                Stopwatch initialization = Stopwatch.StartNew();
                await handler.InitializationTask.WaitAsync(TimeSpan.FromSeconds(options.InitializationTimeoutSeconds));
                initialization.Stop();

                if (!handler.IsInitialized) {
                    throw new InvalidOperationException("Handler initialization did not complete successfully.");
                }

                ResourceInfo resourceInfo = handler.ResourceInfo ?? throw new InvalidOperationException("Handler did not expose Hermes v2 resource diagnostics.");
                Console.WriteLine($"Resource: source={resourceInfo.Source}, revision={resourceInfo.ResourceRevision}, fcs={resourceInfo.FcsCommit}, validation={resourceInfo.ValidationStatus}, resolvedLocations={resourceInfo.ResolvedLocationCount}");

                if (!handler.Scanner.Locations.TryGetValue(Signatures.CHATLOG_KEY, out MemoryLocation? location)) {
                    throw new InvalidOperationException("CHATLOG location was not resolved.");
                }

                IntPtr chatAddress = location.GetAddress();
                Console.WriteLine($"Initialization: {initialization.ElapsedMilliseconds} ms, address=0x{chatAddress.ToInt64():X}");
                if (chatAddress.ToInt64() <= 20 || !handler.Reader.CanGetChatLog()) {
                    throw new InvalidOperationException("Resolved CHATLOG address is not readable.");
                }

                ChatLogResult firstPoll = handler.Reader.GetChatLog();
                ThrowIfObserved(exceptions);
                if (process.HasExited) {
                    throw new InvalidOperationException("FFXIV exited during the first chat read.");
                }

                if (!firstPoll.ChatLogItems.IsEmpty) {
                    throw new InvalidOperationException($"First poll returned {firstPoll.ChatLogItems.Count} historical entries.");
                }

                Console.WriteLine($"First poll: empty, cursor={firstPoll.PreviousArrayIndex}:{firstPoll.PreviousOffset}");

                if (options.RequireCurrentTalk) {
                    ValidateTalk(
                        "CurrentTalk",
                        handler.Reader.CanGetCurrentTalk(),
                        handler.Reader.GetCurrentTalk(),
                        options.PrintTalk);
                }

                if (options.RequireLastTalk) {
                    ValidateTalk(
                        "LastTalk",
                        handler.Reader.CanGetLastTalk(),
                        handler.Reader.GetLastTalk(),
                        options.PrintTalk);
                }

                if (options.RequireTalk) {
                    ValidateTalk(
                        "Talk",
                        handler.Reader.CanGetTalk(),
                        handler.Reader.GetTalk(),
                        options.PrintTalk);
                }

                if (options.RequireBattleTalk) {
                    ValidateBattleTalk(
                        handler.Reader.CanGetBattleTalk(),
                        handler.Reader.GetBattleTalk(),
                        options.PrintTalk);
                }

                if (options.BattleTalkSequenceSeconds > 0) {
                    await RunBattleTalkSequenceProbe(
                        handler,
                        process,
                        exceptions,
                        options,
                        firstPoll.PreviousArrayIndex,
                        firstPoll.PreviousOffset);
                }

                if (options.BattleTalkProbeLayoutPath != null) {
                    await RunBattleTalkContractProbe(handler, process, exceptions, resourceInfo, options);
                }

                if (options.ConcurrentReaders > 1) {
                    await RunConcurrentProbe(
                        handler,
                        process,
                        exceptions,
                        firstPoll.PreviousArrayIndex,
                        firstPoll.PreviousOffset,
                        options.ConcurrentReaders,
                        options.ConcurrentCalls);
                }

                if (options.AttachOnly) {
                    Console.WriteLine("LIVE ATTACH PASS");
                    return 0;
                }

                int arrayIndex = firstPoll.PreviousArrayIndex;
                int offset = firstPoll.PreviousOffset;
                int entryCount = 0;
                int wrapCount = 0;
                HashSet<string> codes = new HashSet<string>(StringComparer.Ordinal);
                Stopwatch polling = Stopwatch.StartNew();
                TimeSpan pollingDuration = TimeSpan.FromSeconds(options.PollSeconds);
                TimeSpan pollingInterval = TimeSpan.FromMilliseconds(options.PollIntervalMilliseconds);
                TimeSpan nextProgress = TimeSpan.FromSeconds(options.ProgressSeconds);
                while (polling.Elapsed < pollingDuration) {
                    TimeSpan remaining = pollingDuration - polling.Elapsed;
                    await Task.Delay(remaining < pollingInterval ? remaining : pollingInterval);
                    if (process.HasExited) {
                        throw new InvalidOperationException("FFXIV exited during polling.");
                    }

                    int previousArrayIndex = arrayIndex;
                    int previousOffset = offset;
                    ChatLogResult poll = handler.Reader.GetChatLog(arrayIndex, offset);
                    ThrowIfObserved(exceptions);
                    if (process.HasExited) {
                        throw new InvalidOperationException("FFXIV exited during a chat read.");
                    }

                    arrayIndex = poll.PreviousArrayIndex;
                    offset = poll.PreviousOffset;
                    if (arrayIndex < previousArrayIndex) {
                        wrapCount++;
                    }

                    int pollEntryCount = poll.ChatLogItems.Count;
                    if (pollEntryCount > 0 && arrayIndex == previousArrayIndex && offset == previousOffset) {
                        throw new InvalidOperationException("A nonempty poll did not advance the chat cursor.");
                    }

                    while (poll.ChatLogItems.TryDequeue(out var item)) {
                        if (!string.Equals(item.PlayerCharacterName, ConfiguredCharacterName, StringComparison.Ordinal)) {
                            throw new InvalidOperationException("Configured character name was not propagated to a chat entry.");
                        }

                        entryCount++;
                        codes.Add(item.Code ?? string.Empty);
                    }

                    if (polling.Elapsed >= nextProgress && polling.Elapsed < pollingDuration) {
                        Console.WriteLine(
                            $"Progress: {polling.Elapsed.TotalSeconds:F1} s, entries={entryCount}, wraps={wrapCount}, codes={string.Join(',', codes.OrderBy(code => code))}, cursor={arrayIndex}:{offset}");
                        nextProgress += TimeSpan.FromSeconds(options.ProgressSeconds);
                    }
                }

                ThrowIfObserved(exceptions);
                if (process.HasExited) {
                    throw new InvalidOperationException("FFXIV exited before live validation completed.");
                }

                Console.WriteLine($"Polling: {polling.Elapsed.TotalSeconds:F1} s, entries={entryCount}, wraps={wrapCount}, codes={string.Join(',', codes.OrderBy(code => code))}, cursor={arrayIndex}:{offset}");
                if (entryCount < options.MinimumEntries) {
                    throw new InvalidOperationException(
                        $"Expected at least {options.MinimumEntries} new chat entries, observed {entryCount}.");
                }

                if (wrapCount < options.MinimumWraps) {
                    throw new InvalidOperationException(
                        $"Expected at least {options.MinimumWraps} chat ring wraps, observed {wrapCount}.");
                }

                string[] missingCodes = options.RequiredCodes.Except(codes, StringComparer.Ordinal).OrderBy(code => code).ToArray();
                if (missingCodes.Length > 0) {
                    throw new InvalidOperationException("Required chat codes were not observed: " + string.Join(',', missingCodes));
                }

                Console.WriteLine("LIVE SMOKE PASS");
                return 0;
            }
            finally {
                if (handler != null) {
                    SharlayanMemoryManager.Instance.RemoveHandler(process.Id);
                }
            }
        }
        catch (Exception exception) {
            Console.Error.WriteLine($"LIVE SMOKE FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static (int Matches, int FailedReads) CountSignatureMatches(Process process, string signature) {
        byte?[] pattern = ParsePattern(signature);
        if (pattern.Length > BufferSize - RegionIncrement + 1) {
            throw new InvalidOperationException("CHATLOG signature exceeds the module scan overlap.");
        }

        ProcessModule module = process.MainModule ?? throw new InvalidOperationException("FFXIV main module is unavailable.");
        IntPtr handle = UnsafeNativeMethods.OpenProcess(UnsafeNativeMethods.ProcessAccessFlags.PROCESS_VM_READ_QUERY, false, (uint) process.Id);
        if (handle == IntPtr.Zero) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open FFXIV for signature validation.");
        }

        try {
            HashSet<long> matches = new HashSet<long>();
            byte[] buffer = new byte[BufferSize];
            int failedReads = 0;
            long moduleSize = module.ModuleMemorySize;
            for (long moduleOffset = 0; moduleOffset < moduleSize; moduleOffset += RegionIncrement) {
                int readSize = (int) Math.Min(BufferSize, moduleSize - moduleOffset);
                IntPtr address = new IntPtr(module.BaseAddress.ToInt64() + moduleOffset);
                if (!UnsafeNativeMethods.ReadProcessMemory(handle, address, buffer, new IntPtr(readSize), out IntPtr bytesReadPointer)) {
                    failedReads++;
                    continue;
                }

                int bytesRead = checked((int) bytesReadPointer.ToInt64());
                if (bytesRead != readSize) {
                    failedReads++;
                    continue;
                }

                int lastStart = bytesRead - pattern.Length;
                for (int index = 0; index <= lastStart; index++) {
                    bool isMatch = true;
                    for (int patternIndex = 0; patternIndex < pattern.Length; patternIndex++) {
                        byte? expected = pattern[patternIndex];
                        if (expected.HasValue && buffer[index + patternIndex] != expected.Value) {
                            isMatch = false;
                            break;
                        }
                    }

                    if (isMatch) {
                        matches.Add(address.ToInt64() + index);
                    }
                }
            }

            return (matches.Count, failedReads);
        }
        finally {
            UnsafeNativeMethods.CloseHandle(handle);
        }
    }

    private static Process FindProcess(int? processId) {
        if (processId.HasValue) {
            Process process = Process.GetProcessById(processId.Value);
            if (!string.Equals(process.ProcessName, "ffxiv_dx11", StringComparison.OrdinalIgnoreCase)) {
                process.Dispose();
                throw new InvalidOperationException($"Process {processId.Value} is not ffxiv_dx11.");
            }

            return process;
        }

        Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
        if (processes.Length != 1) {
            foreach (Process process in processes) {
                process.Dispose();
            }

            throw new InvalidOperationException($"Expected exactly one ffxiv_dx11 process, found {processes.Length}. Use --process-id when multiple clients are running.");
        }

        return processes[0];
    }

    private static byte?[] ParsePattern(string signature) {
        if (string.IsNullOrWhiteSpace(signature) || signature.Length % 2 != 0) {
            throw new InvalidOperationException("CHATLOG signature has an invalid length.");
        }

        byte?[] pattern = new byte?[signature.Length / 2];
        for (int index = 0; index < signature.Length; index += 2) {
            string value = signature.Substring(index, 2);
            pattern[index / 2] = value.Contains('?') || value.Contains('*')
                ? null
                : byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return pattern;
    }

    private static void ThrowIfObserved(ConcurrentQueue<Exception> exceptions) {
        if (exceptions.TryDequeue(out Exception? exception)) {
            throw new InvalidOperationException($"Sharlayan raised {exception.GetType().Name}: {exception.Message}", exception);
        }
    }

    private static async Task RunConcurrentProbe(
        MemoryHandler handler,
        Process process,
        ConcurrentQueue<Exception> exceptions,
        int arrayIndex,
        int offset,
        int readerCount,
        int callsPerReader) {
        using ManualResetEventSlim start = new ManualResetEventSlim();
        Task[] readers = Enumerable.Range(0, readerCount)
            .Select(
                _ => Task.Run(
                    () => {
                        start.Wait();
                        for (int call = 0; call < callsPerReader; call++) {
                            ChatLogResult result = handler.Reader.GetChatLog(arrayIndex, offset);
                            if (!result.ChatLogItems.IsEmpty
                                && result.PreviousArrayIndex == arrayIndex
                                && result.PreviousOffset == offset) {
                                throw new InvalidOperationException("A concurrent chat read returned entries without advancing its cursor.");
                            }

                            while (result.ChatLogItems.TryDequeue(out var item)) {
                                if (!string.Equals(item.PlayerCharacterName, ConfiguredCharacterName, StringComparison.Ordinal)) {
                                    throw new InvalidOperationException("Configured character name was not propagated during concurrent reads.");
                                }
                            }
                        }
                    }))
            .ToArray();
        start.Set();
        await Task.WhenAll(readers);
        ThrowIfObserved(exceptions);
        if (process.HasExited) {
            throw new InvalidOperationException("FFXIV exited during concurrent chat reads.");
        }

        Console.WriteLine($"Concurrent readers: {readerCount} threads x {callsPerReader} calls, PASS");
    }

    private static void ValidateTalk(string label, bool canRead, TalkResult talk, bool printTalk) {
        if (!canRead) {
            throw new InvalidOperationException($"{label} locations were not resolved.");
        }

        if (!talk.IsAvailable || string.IsNullOrEmpty(talk.Text)) {
            throw new InvalidOperationException($"No readable {label} value is available. Open an NPC Talk and retry.");
        }

        Console.WriteLine(
            $"{label}: available, source={talk.Source}, visible={talk.IsVisible}, nameUtf16Length={talk.Name.Length}, textUtf16Length={talk.Text.Length}");
        if (printTalk) {
            Console.WriteLine($"{label} name: {talk.Name}");
            Console.WriteLine($"{label} text: {talk.Text}");
        }
    }

    private static void ValidateBattleTalk(
        bool canRead,
        BattleTalkResult battleTalk,
        bool printTalk) {
        if (!canRead) {
            throw new InvalidOperationException("BattleTalk resource was not resolved.");
        }

        if (!battleTalk.IsAvailable
            || !battleTalk.IsVisible
            || string.IsNullOrEmpty(battleTalk.Text)
            || battleTalk.Sequence < 1) {
            throw new InvalidOperationException(
                "No visible BattleTalk value is available. Open a BattleTalk and retry.");
        }

        Console.WriteLine(
            $"BattleTalk: available, visible, sequence={battleTalk.Sequence}, "
            + $"nameUtf16Length={battleTalk.Name.Length}, textUtf16Length={battleTalk.Text.Length}");
        if (printTalk) {
            Console.WriteLine($"BattleTalk name: {battleTalk.Name}");
            Console.WriteLine($"BattleTalk text: {battleTalk.Text}");
        }
    }

    private static async Task RunBattleTalkSequenceProbe(
        MemoryHandler handler,
        Process process,
        ConcurrentQueue<Exception> exceptions,
        Options options,
        int chatArrayIndex,
        int chatOffset) {
        if (!handler.Reader.CanGetBattleTalk()) {
            throw new InvalidOperationException("BattleTalk resource was not resolved.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan duration = TimeSpan.FromSeconds(options.BattleTalkSequenceSeconds);
        TimeSpan interval = TimeSpan.FromMilliseconds(options.BattleTalkSequenceIntervalMilliseconds);
        string? previous = null;
        long maximumSequence = 0;
        int changes = 0;
        int relevantChatEntries = 0;
        int correlatedChatEntries = 0;
        string visibleText = string.Empty;
        Console.WriteLine(
            $"BattleTalk sequence probe: interval={options.BattleTalkSequenceIntervalMilliseconds}ms, "
            + $"duration={options.BattleTalkSequenceSeconds}s");
        while (stopwatch.Elapsed < duration) {
            if (process.HasExited) {
                throw new InvalidOperationException("FFXIV exited during the BattleTalk sequence probe.");
            }

            BattleTalkResult result = handler.Reader.GetBattleTalk();
            if (result.Sequence < maximumSequence) {
                throw new InvalidOperationException("BattleTalk Sequence moved backwards.");
            }

            maximumSequence = Math.Max(maximumSequence, result.Sequence);
            if (result.IsAvailable) {
                visibleText = result.IsVisible ? result.Text : string.Empty;
            }
            string fingerprint =
                $"{result.IsAvailable}:{result.IsVisible}:{result.Sequence}:{HashText(result.Name)}:{HashText(result.Text)}";
            if (!string.Equals(previous, fingerprint, StringComparison.Ordinal)) {
                previous = fingerprint;
                changes++;
                Console.WriteLine(
                    $"BT-API {stopwatch.Elapsed.TotalSeconds,7:F3}s "
                    + $"available={result.IsAvailable} visible={result.IsVisible} sequence={result.Sequence} "
                    + $"nameLength={result.Name.Length} nameHash={HashText(result.Name)} "
                    + $"textLength={result.Text.Length} textHash={HashText(result.Text)}");
            }

            ChatLogResult chat = handler.Reader.GetChatLog(chatArrayIndex, chatOffset);
            chatArrayIndex = chat.PreviousArrayIndex;
            chatOffset = chat.PreviousOffset;
            while (chat.ChatLogItems.TryDequeue(out var item)) {
                if (!int.TryParse(
                        item.Code,
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out int code)
                    || (code != 0x3D && code != 0x2AB9)) {
                    continue;
                }

                relevantChatEntries++;
                string message = item.Message ?? string.Empty;
                bool correlated = !string.IsNullOrEmpty(visibleText)
                                  && !string.IsNullOrEmpty(message)
                                  && (string.Equals(message, visibleText, StringComparison.Ordinal)
                                      || message.Contains(visibleText, StringComparison.Ordinal)
                                      || visibleText.Contains(message, StringComparison.Ordinal));
                if (correlated) correlatedChatEntries++;
                Console.WriteLine(
                    $"BT-CHAT code={item.Code} messageLength={message.Length} "
                    + $"messageHash={HashText(message)} correlated={correlated}");
            }

            ThrowIfObserved(exceptions);
            TimeSpan remaining = duration - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero) {
                await Task.Delay(remaining < interval ? remaining : interval);
            }
        }

        Console.WriteLine(
            $"BattleTalk sequence probe complete: changes={changes}, maximumSequence={maximumSequence}, "
            + $"relevantChatEntries={relevantChatEntries}, correlatedChatEntries={correlatedChatEntries}");
    }

    private static string HashText(string value) {
        if (string.IsNullOrEmpty(value)) return "-";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private static async Task RunBattleTalkContractProbe(
        MemoryHandler handler,
        Process process,
        ConcurrentQueue<Exception> exceptions,
        ResourceInfo resourceInfo,
        Options options) {
        string json = File.ReadAllText(options.BattleTalkProbeLayoutPath!);
        BattleTalkProbeLayout layout = JsonConvert.DeserializeObject<BattleTalkProbeLayout>(json)
            ?? throw new InvalidDataException("BattleTalk probe layout could not be deserialized.");
        if (!handler.Scanner.Locations.TryGetValue(
                Signatures.CURRENT_TALK_UI_MODULE_POINTER_KEY,
                out MemoryLocation? location)) {
            throw new InvalidOperationException("UI module pointer location was not resolved.");
        }

        if (!string.Equals(resourceInfo.FcsCommit, layout.FcsCommit, StringComparison.OrdinalIgnoreCase)) {
            Console.WriteLine(
                $"BattleTalk probe warning: runtime manifest FCS={resourceInfo.FcsCommit}, probe layout FCS={layout.FcsCommit}");
        }

        IntPtr uiModulePointerAddress = location.GetAddress();
        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan duration = TimeSpan.FromSeconds(options.BattleTalkProbeSeconds);
        TimeSpan interval = TimeSpan.FromMilliseconds(options.BattleTalkProbeIntervalMilliseconds);
        string? previousFingerprint = null;
        int observations = 0;
        int changes = 0;
        Console.WriteLine(
            $"BattleTalk probe: layout={options.BattleTalkProbeLayoutPath}, interval={options.BattleTalkProbeIntervalMilliseconds}ms, duration={options.BattleTalkProbeSeconds}s");
        while (stopwatch.Elapsed < duration) {
            if (process.HasExited) {
                throw new InvalidOperationException("FFXIV exited during the BattleTalk contract probe.");
            }

            BattleTalkProbeObservation observation =
                BattleTalkContractProbe.Read(handler.Peek, uiModulePointerAddress, layout);
            observations++;
            if (!string.Equals(previousFingerprint, observation.Fingerprint, StringComparison.Ordinal)) {
                changes++;
                previousFingerprint = observation.Fingerprint;
                PrintBattleTalkObservation(stopwatch.Elapsed, observation);
            }

            ThrowIfObserved(exceptions);
            TimeSpan remaining = duration - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero) {
                await Task.Delay(remaining < interval ? remaining : interval);
            }
        }

        Console.WriteLine($"BattleTalk probe complete: observations={observations}, changes={changes}");
    }

    private static void PrintBattleTalkObservation(TimeSpan elapsed, BattleTalkProbeObservation observation) {
        BattleTalkProbeAddonObservation addon = observation.Addon;
        Console.WriteLine(
            $"BT {elapsed.TotalSeconds,7:F3}s fp={ShortHash(observation.Fingerprint)} "
            + $"addon=r{Bool(addon.IsReadable)}f{Bool(addon.IsFound)}v{Bool(addon.IsVisible)}y{Bool(addon.IsReady)}"
            + $"/count={addon.AtkValueCount}/strings={FormatStrings(addon.Strings)} "
            + $"number=r{Bool(observation.NumberArray.IsReadable)}/u{observation.NumberArray.UpdateState}"
            + $"/values=[{string.Join(",", observation.NumberArray.Values)}] "
            + $"string=r{Bool(observation.StringArray.IsReadable)}/u{observation.StringArray.UpdateState}"
            + $"/values={FormatStrings(observation.StringArray.Values)} "
            + $"queue={FormatQueue(observation.QueueSlots)}");
    }

    private static string FormatStrings(IReadOnlyDictionary<int, BattleTalkProbeString> values) {
        return "[" + string.Join(
            ",",
            values.Select(pair =>
                $"{pair.Key}:p{pair.Value.Pointer.ToInt64():X}:l{pair.Value.Utf8Length}:h{ShortHash(pair.Value.Hash)}"))
            + "]";
    }

    private static string FormatQueue(IReadOnlyList<BattleTalkProbeQueueSlot> slots) {
        return "[" + string.Join(
            ",",
            slots.Where(slot =>
                    !slot.IsReadable
                    || slot.IsPending
                    || (slot.Name != null && slot.Name.Utf8Length > 0)
                    || (slot.Text != null && slot.Text.Utf8Length > 0))
                .Select(slot =>
                    $"{slot.Index}:r{Bool(slot.IsReadable)}p{Bool(slot.IsPending)}/s{slot.Style}"
                    + $"/n={FormatString(slot.Name)}/t={FormatString(slot.Text)}"
                    + $"/i{slot.Image}/a{slot.Sound}/e{slot.EntityId}"))
            + "]";
    }

    private static string FormatString(BattleTalkProbeString? value) {
        return value == null
            ? "-"
            : $"p{value.Pointer.ToInt64():X}:l{value.Utf8Length}:h{ShortHash(value.Hash)}";
    }

    private static string ShortHash(string value) {
        return string.IsNullOrEmpty(value) ? "-" : value.Substring(0, Math.Min(12, value.Length));
    }

    private static int Bool(bool value) => value ? 1 : 0;

    private sealed class Options {
        public int InitializationTimeoutSeconds { get; private set; } = 30;

        public bool AttachOnly { get; private set; }

        public int BattleTalkProbeIntervalMilliseconds { get; private set; } = 100;

        public string? BattleTalkProbeLayoutPath { get; private set; }

        public int BattleTalkProbeSeconds { get; private set; } = 120;

        public int BattleTalkSequenceIntervalMilliseconds { get; private set; } = 100;

        public int BattleTalkSequenceSeconds { get; private set; }

        public Uri? HermesV2LatestUri { get; private set; }

        public int ConcurrentCalls { get; private set; } = 20;

        public int ConcurrentReaders { get; private set; } = 1;

        public int MinimumEntries { get; private set; } = 1;

        public int MinimumWraps { get; private set; }

        public int PollIntervalMilliseconds { get; private set; } = 250;

        public int PollSeconds { get; private set; } = 30;

        public int ProgressSeconds { get; private set; } = 60;

        public int? ProcessId { get; private set; }

        public string? ManifestPath { get; private set; }

        public bool PrintTalk { get; private set; }

        public bool RemotePreferred { get; private set; }

        public string? ResourceCacheDirectory { get; private set; }

        public HashSet<string> RequiredCodes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public bool RequireCurrentTalk { get; private set; }

        public bool RequireBattleTalk { get; private set; }

        public bool RequireLastTalk { get; private set; }

        public bool RequireTalk { get; private set; }

        public static Options Parse(string[] args) {
            Options options = new Options();
            for (int index = 0; index < args.Length; index++) {
                switch (args[index]) {
                    case "--attach-only":
                        options.AttachOnly = true;
                        break;
                    case "--battle-talk-probe-interval":
                        options.BattleTalkProbeIntervalMilliseconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--battle-talk-probe-layout":
                        options.BattleTalkProbeLayoutPath = Path.GetFullPath(ReadString(args, ref index));
                        break;
                    case "--battle-talk-probe-seconds":
                        options.BattleTalkProbeSeconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--battle-talk-sequence-interval":
                        options.BattleTalkSequenceIntervalMilliseconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--battle-talk-sequence-seconds":
                        options.BattleTalkSequenceSeconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--initialization-timeout":
                        options.InitializationTimeoutSeconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--concurrent-calls":
                        options.ConcurrentCalls = ReadPositiveInt(args, ref index);
                        break;
                    case "--concurrent-readers":
                        options.ConcurrentReaders = ReadPositiveInt(args, ref index);
                        break;
                    case "--minimum-entries":
                        options.MinimumEntries = ReadNonNegativeInt(args, ref index);
                        break;
                    case "--minimum-wraps":
                        options.MinimumWraps = ReadNonNegativeInt(args, ref index);
                        break;
                    case "--poll-interval":
                        options.PollIntervalMilliseconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--poll-seconds":
                        options.PollSeconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--progress-seconds":
                        options.ProgressSeconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--process-id":
                        options.ProcessId = ReadPositiveInt(args, ref index);
                        break;
                    case "--manifest":
                        options.ManifestPath = Path.GetFullPath(ReadString(args, ref index));
                        break;
                    case "--latest-uri":
                        options.HermesV2LatestUri = ReadHttpsUri(args, ref index);
                        break;
                    case "--remote-preferred":
                        options.RemotePreferred = true;
                        break;
                    case "--resource-cache":
                        options.ResourceCacheDirectory = Path.GetFullPath(ReadString(args, ref index));
                        break;
                    case "--required-code":
                        options.RequiredCodes.Add(ReadChatCode(args, ref index));
                        break;
                    case "--require-talk":
                        options.RequireTalk = true;
                        break;
                    case "--require-current-talk":
                        options.RequireCurrentTalk = true;
                        break;
                    case "--require-battle-talk":
                        options.RequireBattleTalk = true;
                        break;
                    case "--require-last-talk":
                        options.RequireLastTalk = true;
                        break;
                    case "--print-talk":
                        options.PrintTalk = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[index]}'.");
                }
            }

            if (options.ManifestPath != null && options.RemotePreferred) {
                throw new ArgumentException("--manifest and --remote-preferred cannot be used together.");
            }

            if (options.ConcurrentReaders > 64) {
                throw new ArgumentOutOfRangeException(nameof(args), "--concurrent-readers cannot exceed 64.");
            }

            if (options.ConcurrentCalls > 10000) {
                throw new ArgumentOutOfRangeException(nameof(args), "--concurrent-calls cannot exceed 10000.");
            }

            if (options.BattleTalkProbeIntervalMilliseconds > 200) {
                throw new ArgumentOutOfRangeException(
                    nameof(args),
                    "--battle-talk-probe-interval cannot exceed 200 ms.");
            }

            if (options.BattleTalkSequenceIntervalMilliseconds > 200) {
                throw new ArgumentOutOfRangeException(
                    nameof(args),
                    "--battle-talk-sequence-interval cannot exceed 200 ms.");
            }

            return options;
        }

        private static int ReadPositiveInt(string[] args, ref int index) {
            int value = ReadNonNegativeInt(args, ref index);
            if (value == 0) {
                throw new ArgumentOutOfRangeException(nameof(args), "Value must be positive.");
            }

            return value;
        }

        private static int ReadNonNegativeInt(string[] args, ref int index) {
            if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0) {
                throw new ArgumentException("Expected a non-negative integer argument value.");
            }

            return value;
        }

        private static string ReadString(string[] args, ref int index) {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index])) {
                throw new ArgumentException("Expected a non-empty argument value.");
            }

            return args[index];
        }

        private static string ReadChatCode(string[] args, ref int index) {
            string value = ReadString(args, ref index).ToUpperInvariant();
            if (value.Length != 4 || value.Any(character => !Uri.IsHexDigit(character))) {
                throw new ArgumentException("Expected a four-character hexadecimal chat code.");
            }

            return value;
        }

        private static Uri ReadHttpsUri(string[] args, ref int index) {
            string value = ReadString(args, ref index);
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)) {
                throw new ArgumentException("Expected an absolute HTTPS URI without query or fragment.");
            }

            return uri;
        }
    }
}
