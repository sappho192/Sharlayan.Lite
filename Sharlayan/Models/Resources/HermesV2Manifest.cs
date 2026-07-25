namespace Sharlayan.Models.Resources {
    using System.Collections.Generic;

    using Newtonsoft.Json;

    internal sealed class HermesV2Manifest {
        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; }

        [JsonProperty("compatibility", Required = Required.Always)]
        public HermesCompatibility Compatibility { get; set; }

        [JsonProperty("source", Required = Required.Always)]
        public HermesSource Source { get; set; }

        [JsonProperty("platform", Required = Required.Always)]
        public HermesPlatform Platform { get; set; }

        [JsonProperty("roots", Required = Required.Always)]
        public HermesRoots Roots { get; set; }

        [JsonProperty("resources", Required = Required.Always)]
        public HermesResources Resources { get; set; }

        [JsonProperty("validation", Required = Required.Always)]
        public HermesValidation Validation { get; set; }
    }

    internal sealed class HermesCompatibility {
        [JsonProperty("minimumSharlayanVersion", Required = Required.Always)] public string MinimumSharlayanVersion { get; set; }
        [JsonProperty("pointerResolverVersion", Required = Required.Always)] public int PointerResolverVersion { get; set; }
    }

    internal sealed class HermesSource {
        [JsonProperty("fcsRepository", Required = Required.Always)] public string FcsRepository { get; set; }
        [JsonProperty("fcsCommit", Required = Required.Always)] public string FcsCommit { get; set; }
        [JsonProperty("generatorRepository", Required = Required.Always)] public string GeneratorRepository { get; set; }
        [JsonProperty("generatorCommit", Required = Required.Always)] public string GeneratorCommit { get; set; }
    }

    internal sealed class HermesPlatform {
        [JsonProperty("process", Required = Required.Always)] public string Process { get; set; }
        [JsonProperty("architecture", Required = Required.Always)] public string Architecture { get; set; }
    }

    internal sealed class HermesRoots {
        [JsonProperty("framework", Required = Required.Always)] public HermesFrameworkRoot Framework { get; set; }
    }

    internal sealed class HermesFrameworkRoot {
        [JsonProperty("pattern", Required = Required.Always)] public string Pattern { get; set; }
        [JsonProperty("relativeFollowOffset", Required = Required.Always)] public int RelativeFollowOffset { get; set; }
        [JsonProperty("isPointer", Required = Required.Always)] public bool IsPointer { get; set; }
    }

    internal sealed class HermesResources {
        [JsonProperty("chatLog", Required = Required.Always)] public HermesChatLogResource ChatLog { get; set; }
        [JsonProperty("talk", Required = Required.Always)] public HermesTalkResource Talk { get; set; }
        [JsonProperty("currentTalk", Required = Required.Always)] public HermesCurrentTalkResource CurrentTalk { get; set; }
    }

    internal sealed class HermesChatLogResource {
        [JsonProperty("root", Required = Required.Always)] public string Root { get; set; }
        [JsonProperty("uiModuleOffset", Required = Required.Always)] public int UiModuleOffset { get; set; }
        [JsonProperty("raptureLogModuleOffset", Required = Required.Always)] public int RaptureLogModuleOffset { get; set; }
        [JsonProperty("indexVectorOffset", Required = Required.Always)] public int IndexVectorOffset { get; set; }
        [JsonProperty("dataVectorOffset", Required = Required.Always)] public int DataVectorOffset { get; set; }
    }

    internal sealed class HermesTalkResource {
        [JsonProperty("root", Required = Required.Always)] public string Root { get; set; }
        [JsonProperty("semantics", Required = Required.Always)] public string Semantics { get; set; }
        [JsonProperty("uiModuleOffset", Required = Required.Always)] public int UiModuleOffset { get; set; }
        [JsonProperty("nameOffset", Required = Required.Always)] public int NameOffset { get; set; }
        [JsonProperty("textOffset", Required = Required.Always)] public int TextOffset { get; set; }
        [JsonProperty("utf8String", Required = Required.Always)] public HermesUtf8StringLayout Utf8String { get; set; }
    }

    internal sealed class HermesUtf8StringLayout {
        [JsonProperty("stringPointerOffset", Required = Required.Always)] public int StringPointerOffset { get; set; }
        [JsonProperty("bufferUsedOffset", Required = Required.Always)] public int BufferUsedOffset { get; set; }
        [JsonProperty("lengthSource", Required = Required.Always)] public string LengthSource { get; set; }
    }

    internal sealed class HermesCurrentTalkResource {
        [JsonProperty("root", Required = Required.Always)] public string Root { get; set; }
        [JsonProperty("semantics", Required = Required.Always)] public string Semantics { get; set; }
        [JsonProperty("uiModuleOffset", Required = Required.Always)] public int UiModuleOffset { get; set; }
        [JsonProperty("raptureAtkModuleOffset", Required = Required.Always)] public int RaptureAtkModuleOffset { get; set; }
        [JsonProperty("raptureAtkUnitManagerOffset", Required = Required.Always)] public int RaptureAtkUnitManagerOffset { get; set; }
        [JsonProperty("allLoadedUnitsListOffset", Required = Required.Always)] public int AllLoadedUnitsListOffset { get; set; }
        [JsonProperty("atkUnitList", Required = Required.Always)] public HermesAtkUnitListLayout AtkUnitList { get; set; }
        [JsonProperty("atkUnitBase", Required = Required.Always)] public HermesAtkUnitBaseLayout AtkUnitBase { get; set; }
        [JsonProperty("atkValue", Required = Required.Always)] public HermesAtkValueLayout AtkValue { get; set; }
        [JsonProperty("addonName", Required = Required.Always)] public string AddonName { get; set; }
        [JsonProperty("textValueIndex", Required = Required.Always)] public int TextValueIndex { get; set; }
        [JsonProperty("nameValueIndex", Required = Required.Always)] public int NameValueIndex { get; set; }
    }

    internal sealed class HermesAtkUnitListLayout {
        [JsonProperty("entriesOffset", Required = Required.Always)] public int EntriesOffset { get; set; }
        [JsonProperty("countOffset", Required = Required.Always)] public int CountOffset { get; set; }
        [JsonProperty("capacity", Required = Required.Always)] public int Capacity { get; set; }
        [JsonProperty("entrySize", Required = Required.Always)] public int EntrySize { get; set; }
    }

    internal sealed class HermesAtkUnitBaseLayout {
        [JsonProperty("nameOffset", Required = Required.Always)] public int NameOffset { get; set; }
        [JsonProperty("nameCapacity", Required = Required.Always)] public int NameCapacity { get; set; }
        [JsonProperty("visibilityStateOffset", Required = Required.Always)] public int VisibilityStateOffset { get; set; }
        [JsonProperty("visibilityMask", Required = Required.Always)] public uint VisibilityMask { get; set; }
        [JsonProperty("readinessOffset", Required = Required.Always)] public int ReadinessOffset { get; set; }
        [JsonProperty("readinessMask", Required = Required.Always)] public uint ReadinessMask { get; set; }
        [JsonProperty("atkValuesPointerOffset", Required = Required.Always)] public int AtkValuesPointerOffset { get; set; }
        [JsonProperty("atkValuesCountOffset", Required = Required.Always)] public int AtkValuesCountOffset { get; set; }
    }

    internal sealed class HermesAtkValueLayout {
        [JsonProperty("size", Required = Required.Always)] public int Size { get; set; }
        [JsonProperty("typeOffset", Required = Required.Always)] public int TypeOffset { get; set; }
        [JsonProperty("valueOffset", Required = Required.Always)] public int ValueOffset { get; set; }
        [JsonProperty("allowedStringTypes", Required = Required.Always)] public List<int> AllowedStringTypes { get; set; }
    }

    internal sealed class HermesValidation {
        [JsonProperty("status", Required = Required.Always)] public string Status { get; set; }
        [JsonProperty("gameVersion")] public string GameVersion { get; set; }
        [JsonProperty("executableSha256")] public string ExecutableSha256 { get; set; }
        [JsonProperty("verifierCommit")] public string VerifierCommit { get; set; }
    }

    internal sealed class HermesLatestPointer {
        [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion { get; set; }
        [JsonProperty("resourceRevision", Required = Required.Always)] public string ResourceRevision { get; set; }
        [JsonProperty("manifest", Required = Required.Always)] public string Manifest { get; set; }
        [JsonProperty("fcsCommit", Required = Required.Always)] public string FcsCommit { get; set; }
        [JsonProperty("publishedAt", Required = Required.Always)] public string PublishedAt { get; set; }
    }

    internal sealed class HermesManifestCandidate {
        internal HermesManifestCandidate(HermesV2Manifest manifest, byte[] bytes, ResourceInfo info) {
            this.Manifest = manifest;
            this.Bytes = bytes;
            this.Info = info;
        }

        internal HermesV2Manifest Manifest { get; }
        internal byte[] Bytes { get; }
        internal ResourceInfo Info { get; }
    }
}
