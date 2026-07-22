namespace Sharlayan.LiveSmoke;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

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
            };
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

                if (options.RequireTalk) {
                    if (!handler.Reader.CanGetLastTalk()) {
                        throw new InvalidOperationException("Talk locations were not resolved.");
                    }

                    TalkResult talk = handler.Reader.GetLastTalk();
                    if (!talk.IsAvailable || string.IsNullOrEmpty(talk.Text)) {
                        throw new InvalidOperationException("No readable standard Talk value is available. Open an NPC Talk and retry.");
                    }

                    Console.WriteLine($"Talk: available, nameUtf16Length={talk.Name.Length}, textUtf16Length={talk.Text.Length}");
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
                }

                ThrowIfObserved(exceptions);
                if (process.HasExited) {
                    throw new InvalidOperationException("FFXIV exited before live validation completed.");
                }

                Console.WriteLine($"Polling: {polling.Elapsed.TotalSeconds:F1} s, entries={entryCount}, wraps={wrapCount}, codes={string.Join(',', codes.OrderBy(code => code))}, cursor={arrayIndex}:{offset}");
                if (entryCount == 0) {
                    throw new InvalidOperationException("No new chat entry was observed during the polling window.");
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

    private sealed class Options {
        public int InitializationTimeoutSeconds { get; private set; } = 30;

        public bool AttachOnly { get; private set; }

        public int PollIntervalMilliseconds { get; private set; } = 250;

        public int PollSeconds { get; private set; } = 30;

        public int? ProcessId { get; private set; }

        public string? ManifestPath { get; private set; }

        public bool RequireTalk { get; private set; }

        public static Options Parse(string[] args) {
            Options options = new Options();
            for (int index = 0; index < args.Length; index++) {
                switch (args[index]) {
                    case "--attach-only":
                        options.AttachOnly = true;
                        break;
                    case "--initialization-timeout":
                        options.InitializationTimeoutSeconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--poll-interval":
                        options.PollIntervalMilliseconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--poll-seconds":
                        options.PollSeconds = ReadPositiveInt(args, ref index);
                        break;
                    case "--process-id":
                        options.ProcessId = ReadPositiveInt(args, ref index);
                        break;
                    case "--manifest":
                        options.ManifestPath = ReadString(args, ref index);
                        break;
                    case "--require-talk":
                        options.RequireTalk = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[index]}'.");
                }
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

            return Path.GetFullPath(args[index]);
        }
    }
}
