namespace Sharlayan.Models.Resources {
    public enum ResourceMode {
        EmbeddedOnly,
        RemotePreferred,
    }

    public enum ResourceSource {
        Local,
        Remote,
        Cache,
        Embedded,
    }

    public sealed class ResourceInfo {
        internal ResourceInfo(
            ResourceSource source,
            string resourceRevision,
            string fcsCommit,
            string generatorCommit,
            int schemaVersion,
            string validationStatus,
            string fallbackReason,
            int resolvedLocationCount = 0) {
            this.Source = source;
            this.ResourceRevision = resourceRevision;
            this.FcsCommit = fcsCommit;
            this.GeneratorCommit = generatorCommit;
            this.SchemaVersion = schemaVersion;
            this.ValidationStatus = validationStatus;
            this.FallbackReason = fallbackReason ?? string.Empty;
            this.ResolvedLocationCount = resolvedLocationCount;
        }

        public ResourceSource Source { get; }
        public string ResourceRevision { get; }
        public string FcsCommit { get; }
        public string GeneratorCommit { get; }
        public int SchemaVersion { get; }
        public string ValidationStatus { get; }
        public string FallbackReason { get; }
        public int ResolvedLocationCount { get; }
    }
}
