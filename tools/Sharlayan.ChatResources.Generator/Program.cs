namespace Sharlayan.ChatResources.Generator;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.Log;

using InteropGenerator.Runtime.Attributes;

internal static class Program {
    private static int Main() {
        string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        string fcsRoot = Path.Combine(repositoryRoot, "external", "FFXIVClientStructs");
        string outputPath = Path.Combine(repositoryRoot, "Sharlayan", "Resources", "GeneratedChatResources.g.cs");

        MethodInfo instanceMethod = typeof(Framework).GetMethod(nameof(Framework.Instance), BindingFlags.Public | BindingFlags.Static)
                                    ?? throw new MissingMethodException(typeof(Framework).FullName, nameof(Framework.Instance));
        StaticAddressAttribute staticAddress = instanceMethod.GetCustomAttribute<StaticAddressAttribute>()
                                               ?? throw new InvalidOperationException("Framework.Instance has no StaticAddressAttribute.");
        if (staticAddress.RelativeFollowOffsets.Length != 1) {
            throw new InvalidOperationException("The Sharlayan pointer path supports exactly one relative follow offset.");
        }

        string pattern = NormalizePattern(staticAddress.Signature);
        int patternLength = pattern.Length / 2;
        int relativeFollowOffset = staticAddress.RelativeFollowOffsets[0];
        if (relativeFollowOffset > patternLength) {
            throw new InvalidOperationException("Framework relative follow offset exceeds its signature length.");
        }

        if (!staticAddress.IsPointer) {
            throw new InvalidOperationException("Framework.Instance must resolve through a pointer slot.");
        }

        long rewindOffset = -(patternLength - relativeFollowOffset);
        int uiModuleOffset = GetFieldOffset(typeof(Framework), "UIModule");
        int raptureLogModuleOffset = GetFieldOffset(typeof(UIModule), "RaptureLogModule");
        int indexVectorOffset = GetFieldOffset(typeof(LogModule), nameof(LogModule.LogMessageIndex));
        int dataVectorOffset = GetFieldOffset(typeof(LogModule), nameof(LogModule.LogMessageData));
        string fcsCommit = GetGitCommit(fcsRoot);
        string generated = GenerateSource(
            fcsCommit,
            pattern,
            rewindOffset,
            uiModuleOffset,
            raptureLogModuleOffset,
            indexVectorOffset,
            dataVectorOffset);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (!File.Exists(outputPath) || File.ReadAllText(outputPath) != generated) {
            File.WriteAllText(outputPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"Updated {Path.GetRelativePath(repositoryRoot, outputPath)} from FCS {fcsCommit}.");
        }
        else {
            Console.WriteLine($"{Path.GetRelativePath(repositoryRoot, outputPath)} is current for FCS {fcsCommit}.");
        }

        return 0;
    }

    private static int GetFieldOffset(Type type, string fieldName) {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                          ?? throw new MissingFieldException(type.FullName, fieldName);
        FieldOffsetAttribute offset = field.GetCustomAttribute<FieldOffsetAttribute>()
                                      ?? throw new InvalidOperationException($"{type.FullName}.{fieldName} has no FieldOffsetAttribute.");
        return offset.Value;
    }

    private static string GetGitCommit(string repositoryPath) {
        ProcessStartInfo startInfo = new ProcessStartInfo("git") {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"safe.directory={repositoryPath.Replace('\\', '/')}");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repositoryPath);
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) {
            throw new InvalidOperationException($"Could not resolve the FCS commit: {error}");
        }

        return output;
    }

    private static string FindRepositoryRoot(string startPath) {
        DirectoryInfo? directory = new DirectoryInfo(startPath);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "Sharlayan.sln"))) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root containing Sharlayan.sln.");
    }

    private static string NormalizePattern(string pattern) {
        return pattern.Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
    }

    private static string GenerateSource(
        string fcsCommit,
        string pattern,
        long rewindOffset,
        int uiModuleOffset,
        int raptureLogModuleOffset,
        int indexVectorOffset,
        int dataVectorOffset) {
        return string.Join(
                   "\n",
                   "// <auto-generated />",
                   string.Empty,
                   "namespace Sharlayan.Resources {",
                   "    using System.Collections.Generic;",
                   string.Empty,
                   "    using Signature = Sharlayan.Models.Signature;",
                   "    using ChatLogPointersStructure = Sharlayan.Models.Structures.ChatLogPointers;",
                   "    using StructuresContainer = Sharlayan.Models.Structures.StructuresContainer;",
                   string.Empty,
                   "    internal static class GeneratedChatResources {",
                   $"        internal const string FcsCommit = \"{fcsCommit}\";",
                   string.Empty,
                   "        internal static Signature[] CreateSignatures() {",
                   "            return new[] {",
                   "                new Signature {",
                   "                    ASMSignature = true,",
                   "                    Key = global::Sharlayan.Signatures.CHATLOG_KEY,",
                   $"                    PointerPath = new List<long> {{ {rewindOffset}, 0, 0x{uiModuleOffset:X}, 0x{raptureLogModuleOffset:X} }},",
                   $"                    Value = \"{pattern}\",",
                   "                },",
                   "            };",
                   "        }",
                   string.Empty,
                   "        internal static StructuresContainer CreateStructures() {",
                   "            return new StructuresContainer {",
                   "                ChatLogPointers = new ChatLogPointersStructure {",
                   $"                    OffsetArrayStart = 0x{indexVectorOffset:X},",
                   $"                    OffsetArrayPos = 0x{indexVectorOffset + 8:X},",
                   $"                    OffsetArrayEnd = 0x{indexVectorOffset + 16:X},",
                   $"                    LogStart = 0x{dataVectorOffset:X},",
                   $"                    LogNext = 0x{dataVectorOffset + 8:X},",
                   $"                    LogEnd = 0x{dataVectorOffset + 16:X},",
                   "                },",
                   "            };",
                   "        }",
                   "    }",
                   "}")
               + "\n";
    }
}
