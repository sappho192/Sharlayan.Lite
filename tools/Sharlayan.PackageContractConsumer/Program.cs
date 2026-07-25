namespace Sharlayan.PackageContractConsumer;

using System.Reflection;

using Sharlayan;
using Sharlayan.Models.ReadResults;

internal static class Program {
    public static int Main(string[] args) {
        string expectedVersion = args.Length == 1
            ? args[0]
            : throw new ArgumentException("Expected the package version as the only argument.");
        string informationalVersion =
            typeof(Reader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("Sharlayan assembly has no informational version.");
        string actualVersion = informationalVersion.Split('+')[0];
        if (!string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Expected Sharlayan assembly version {expectedVersion}, found {actualVersion}.");
        }

        AssertPublicMethod(nameof(Reader.CanGetTalk), typeof(bool));
        AssertPublicMethod(nameof(Reader.GetTalk), typeof(TalkResult));
        AssertPublicMethod(nameof(Reader.GetCurrentTalk), typeof(TalkResult));
        AssertPublicMethod(nameof(Reader.GetLastTalk), typeof(TalkResult));
        AssertPublicProperty(nameof(TalkResult.Source), typeof(TalkSource));
        AssertPublicProperty(nameof(TalkResult.IsVisible), typeof(bool));
        AssertPublicProperty(nameof(TalkResult.IsAvailable), typeof(bool));
        AssertPublicProperty(nameof(TalkResult.Name), typeof(string));
        AssertPublicProperty(nameof(TalkResult.Text), typeof(string));

        Console.WriteLine(
            $"PACKAGE CONTRACT PASS: Sharlayan.Lite {actualVersion}, current-first Talk API and result properties available.");
        return 0;
    }

    private static void CompilePublicContract(Reader reader, TalkResult result) {
        _ = reader.CanGetTalk();
        _ = reader.GetTalk();
        _ = reader.GetCurrentTalk();
        _ = reader.GetLastTalk();
        _ = result.Source;
        _ = result.IsVisible;
        _ = result.IsAvailable;
        _ = result.Name;
        _ = result.Text;
    }

    private static void AssertPublicMethod(string name, Type returnType) {
        MethodInfo method = typeof(Reader).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
                            ?? throw new MissingMethodException(typeof(Reader).FullName, name);
        if (method.GetParameters().Length != 0 || method.ReturnType != returnType) {
            throw new InvalidOperationException($"Reader.{name} has an unexpected public signature.");
        }
    }

    private static void AssertPublicProperty(string name, Type propertyType) {
        PropertyInfo property = typeof(TalkResult).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                                ?? throw new MissingMemberException(typeof(TalkResult).FullName, name);
        if (property.PropertyType != propertyType || property.GetMethod?.IsPublic != true) {
            throw new InvalidOperationException($"TalkResult.{name} has an unexpected public signature.");
        }
    }
}
