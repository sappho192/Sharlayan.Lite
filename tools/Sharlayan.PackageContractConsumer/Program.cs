namespace Sharlayan.PackageContractConsumer;

using System.Reflection;

using Sharlayan;
using Sharlayan.Models.ReadResults;

internal static class Program {
    public static int Main(string[] args) {
        string expectedVersion = args.Length == 2
            ? args[0]
            : throw new ArgumentException("Expected package version and repository commit arguments.");
        string expectedCommit = args[1];
        string informationalVersion =
            typeof(Reader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("Sharlayan assembly has no informational version.");
        string actualVersion = informationalVersion.Split('+')[0];
        string expectedInformationalVersion = $"{expectedVersion}+{expectedCommit}";
        if (!string.Equals(informationalVersion, expectedInformationalVersion, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Expected Sharlayan assembly informational version {expectedInformationalVersion}, found {informationalVersion}.");
        }

        AssertPublicMethod(nameof(Reader.CanGetTalk), typeof(bool));
        AssertPublicMethod(nameof(Reader.GetTalk), typeof(TalkResult));
        AssertPublicMethod(nameof(Reader.GetCurrentTalk), typeof(TalkResult));
        AssertPublicMethod(nameof(Reader.GetLastTalk), typeof(TalkResult));
        AssertPublicMethod(nameof(Reader.CanGetBattleTalk), typeof(bool));
        AssertPublicMethod(nameof(Reader.GetBattleTalk), typeof(BattleTalkResult));
        AssertPublicProperty<TalkResult>(nameof(TalkResult.Source), typeof(TalkSource));
        AssertPublicProperty<TalkResult>(nameof(TalkResult.IsVisible), typeof(bool));
        AssertPublicProperty<TalkResult>(nameof(TalkResult.IsAvailable), typeof(bool));
        AssertPublicProperty<TalkResult>(nameof(TalkResult.Name), typeof(string));
        AssertPublicProperty<TalkResult>(nameof(TalkResult.Text), typeof(string));
        AssertPublicProperty<BattleTalkResult>(nameof(BattleTalkResult.IsAvailable), typeof(bool));
        AssertPublicProperty<BattleTalkResult>(nameof(BattleTalkResult.IsVisible), typeof(bool));
        AssertPublicProperty<BattleTalkResult>(nameof(BattleTalkResult.Name), typeof(string));
        AssertPublicProperty<BattleTalkResult>(nameof(BattleTalkResult.Text), typeof(string));
        AssertPublicProperty<BattleTalkResult>(nameof(BattleTalkResult.Sequence), typeof(long));

        Console.WriteLine(
            $"PACKAGE CONTRACT PASS: Sharlayan.Lite {actualVersion}, Talk and BattleTalk APIs available.");
        return 0;
    }

    private static void CompilePublicContract(
        Reader reader,
        TalkResult result,
        BattleTalkResult battleTalk) {
        _ = reader.CanGetTalk();
        _ = reader.GetTalk();
        _ = reader.GetCurrentTalk();
        _ = reader.GetLastTalk();
        _ = result.Source;
        _ = result.IsVisible;
        _ = result.IsAvailable;
        _ = result.Name;
        _ = result.Text;
        _ = reader.CanGetBattleTalk();
        _ = reader.GetBattleTalk();
        _ = battleTalk.IsAvailable;
        _ = battleTalk.IsVisible;
        _ = battleTalk.Name;
        _ = battleTalk.Text;
        _ = battleTalk.Sequence;
    }

    private static void AssertPublicMethod(string name, Type returnType) {
        MethodInfo method = typeof(Reader).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
                            ?? throw new MissingMethodException(typeof(Reader).FullName, name);
        if (method.GetParameters().Length != 0 || method.ReturnType != returnType) {
            throw new InvalidOperationException($"Reader.{name} has an unexpected public signature.");
        }
    }

    private static void AssertPublicProperty<TResult>(string name, Type propertyType) {
        PropertyInfo property = typeof(TResult).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                                ?? throw new MissingMemberException(typeof(TResult).FullName, name);
        if (property.PropertyType != propertyType || property.GetMethod?.IsPublic != true) {
            throw new InvalidOperationException($"{typeof(TResult).Name}.{name} has an unexpected public signature.");
        }
    }
}
