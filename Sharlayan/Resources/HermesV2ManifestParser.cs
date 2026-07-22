namespace Sharlayan.Resources {
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    using Sharlayan.Models.Resources;

    internal static class HermesV2ManifestParser {
        internal const int MaximumManifestBytes = 128 * 1024;
        internal const int MaximumLatestBytes = 16 * 1024;

        internal static HermesV2Manifest ParseManifest(byte[] bytes, string expectedRevision, string clientVersion, bool allowCandidate) {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumManifestBytes) {
                throw new InvalidDataException("Hermes manifest has an invalid response size.");
            }

            string actualRevision = CalculateRevision(bytes);
            if (!string.IsNullOrEmpty(expectedRevision) && !string.Equals(expectedRevision, actualRevision, StringComparison.Ordinal)) {
                throw new InvalidDataException("Hermes manifest revision hash does not match the downloaded bytes.");
            }

            HermesV2Manifest manifest = DeserializeStrict<HermesV2Manifest>(bytes);
            HermesV2ManifestValidator.Validate(manifest, clientVersion, allowCandidate);
            return manifest;
        }

        internal static HermesLatestPointer ParseLatest(byte[] bytes) {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumLatestBytes) {
                throw new InvalidDataException("Hermes latest pointer has an invalid response size.");
            }

            HermesLatestPointer latest = DeserializeStrict<HermesLatestPointer>(bytes);
            HermesV2ManifestValidator.ValidateLatest(latest);
            return latest;
        }

        internal static string CalculateRevision(byte[] bytes) {
            using (SHA256 sha256 = SHA256.Create()) {
                byte[] hash = sha256.ComputeHash(bytes);
                StringBuilder output = new StringBuilder("sha256:");
                foreach (byte value in hash) {
                    output.Append(value.ToString("x2"));
                }

                return output.ToString();
            }
        }

        private static T DeserializeStrict<T>(byte[] bytes) {
            try {
                string json = new UTF8Encoding(false, true).GetString(bytes);
                using (JsonTextReader reader = new JsonTextReader(new StringReader(json))) {
                    reader.DateParseHandling = DateParseHandling.None;
                    JToken token = JToken.Load(reader, new JsonLoadSettings {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    });
                    if (reader.Read()) {
                        throw new InvalidDataException("Hermes JSON contains trailing content.");
                    }

                    if (!(token is JObject root)) {
                        throw new InvalidDataException("Hermes JSON root must be an object.");
                    }

                    if (typeof(T) == typeof(HermesV2Manifest)) ValidateManifestProperties(root);
                    else if (typeof(T) == typeof(HermesLatestPointer)) ValidateLatestProperties(root);
                    JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings {
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                    });
                    T result = token.ToObject<T>(serializer);
                    return result ?? throw new InvalidDataException("Hermes JSON deserialized to null.");
                }
            }
            catch (JsonException exception) {
                throw new InvalidDataException("Hermes JSON is invalid.", exception);
            }
        }

        private static void ValidateManifestProperties(JObject root) {
            ValidateObject(root, "$", "schemaVersion", "compatibility", "source", "platform", "roots", "resources", "validation");
            RequireInteger(root, "schemaVersion", "$.");

            JObject compatibility = RequireObject(root, "compatibility", "$.");
            ValidateObject(compatibility, "$.compatibility", "minimumSharlayanVersion", "pointerResolverVersion");
            RequireString(compatibility, "minimumSharlayanVersion", "$.compatibility.");
            RequireInteger(compatibility, "pointerResolverVersion", "$.compatibility.");

            JObject source = RequireObject(root, "source", "$.");
            ValidateObject(source, "$.source", "fcsRepository", "fcsCommit", "generatorRepository", "generatorCommit");
            RequireString(source, "fcsRepository", "$.source.");
            RequireString(source, "fcsCommit", "$.source.");
            RequireString(source, "generatorRepository", "$.source.");
            RequireString(source, "generatorCommit", "$.source.");

            JObject platform = RequireObject(root, "platform", "$.");
            ValidateObject(platform, "$.platform", "process", "architecture");
            RequireString(platform, "process", "$.platform.");
            RequireString(platform, "architecture", "$.platform.");

            JObject roots = RequireObject(root, "roots", "$.");
            ValidateObject(roots, "$.roots", "framework");
            JObject framework = RequireObject(roots, "framework", "$.roots.");
            ValidateObject(framework, "$.roots.framework", "pattern", "relativeFollowOffset", "isPointer");
            RequireString(framework, "pattern", "$.roots.framework.");
            RequireInteger(framework, "relativeFollowOffset", "$.roots.framework.");
            RequireBoolean(framework, "isPointer", "$.roots.framework.");

            JObject resources = RequireObject(root, "resources", "$.");
            JObject chatLog = RequireObject(resources, "chatLog", "$.resources.");
            ValidateObject(chatLog, "$.resources.chatLog", "root", "uiModuleOffset", "raptureLogModuleOffset", "indexVectorOffset", "dataVectorOffset");
            RequireString(chatLog, "root", "$.resources.chatLog.");
            RequireInteger(chatLog, "uiModuleOffset", "$.resources.chatLog.");
            RequireInteger(chatLog, "raptureLogModuleOffset", "$.resources.chatLog.");
            RequireInteger(chatLog, "indexVectorOffset", "$.resources.chatLog.");
            RequireInteger(chatLog, "dataVectorOffset", "$.resources.chatLog.");

            JObject talk = RequireObject(resources, "talk", "$.resources.");
            ValidateObject(talk, "$.resources.talk", "root", "semantics", "uiModuleOffset", "nameOffset", "textOffset", "utf8String");
            RequireString(talk, "root", "$.resources.talk.");
            RequireString(talk, "semantics", "$.resources.talk.");
            RequireInteger(talk, "uiModuleOffset", "$.resources.talk.");
            RequireInteger(talk, "nameOffset", "$.resources.talk.");
            RequireInteger(talk, "textOffset", "$.resources.talk.");
            JObject utf8String = RequireObject(talk, "utf8String", "$.resources.talk.");
            ValidateObject(utf8String, "$.resources.talk.utf8String", "stringPointerOffset", "bufferUsedOffset", "stringLengthOffset");
            RequireInteger(utf8String, "stringPointerOffset", "$.resources.talk.utf8String.");
            RequireInteger(utf8String, "bufferUsedOffset", "$.resources.talk.utf8String.");
            RequireInteger(utf8String, "stringLengthOffset", "$.resources.talk.utf8String.");

            JObject validation = RequireObject(root, "validation", "$.");
            string status = RequireString(validation, "status", "$.validation.");
            if (status == "candidate") {
                ValidateObject(validation, "$.validation", "status");
            }
            else {
                ValidateObject(validation, "$.validation", "status", "gameVersion", "executableSha256", "verifierCommit");
                RequireOptionalString(validation, "gameVersion", "$.validation.");
                RequireOptionalString(validation, "executableSha256", "$.validation.");
                RequireOptionalString(validation, "verifierCommit", "$.validation.");
            }
        }

        private static void ValidateLatestProperties(JObject root) {
            ValidateObject(root, "$", "schemaVersion", "resourceRevision", "manifest", "fcsCommit", "publishedAt");
            RequireInteger(root, "schemaVersion", "$.");
            RequireString(root, "resourceRevision", "$.");
            RequireString(root, "manifest", "$.");
            RequireString(root, "fcsCommit", "$.");
            RequireString(root, "publishedAt", "$.");
        }

        private static void ValidateObject(JObject value, string path, params string[] allowedProperties) {
            if (value == null) throw new InvalidDataException(path + " must be an object.");
            foreach (JProperty property in value.Properties()) {
                if (Array.IndexOf(allowedProperties, property.Name) < 0) {
                    throw new InvalidDataException("Unexpected Hermes property: " + path + "." + property.Name);
                }
            }
        }

        private static JObject RequireObject(JObject value, string propertyName, string path) {
            JToken token = value[propertyName];
            if (!(token is JObject result)) {
                throw new InvalidDataException(path + propertyName + " must be an object.");
            }

            return result;
        }

        private static string RequireString(JObject value, string propertyName, string path) {
            JToken token = value[propertyName];
            if (token == null || token.Type != JTokenType.String) {
                throw new InvalidDataException(path + propertyName + " must be a string.");
            }

            return token.Value<string>();
        }

        private static void RequireOptionalString(JObject value, string propertyName, string path) {
            if (value.Property(propertyName) != null) {
                RequireString(value, propertyName, path);
            }
        }

        private static void RequireInteger(JObject value, string propertyName, string path) {
            JToken token = value[propertyName];
            if (token == null || token.Type != JTokenType.Integer) {
                throw new InvalidDataException(path + propertyName + " must be an integer.");
            }
        }

        private static void RequireBoolean(JObject value, string propertyName, string path) {
            JToken token = value[propertyName];
            if (token == null || token.Type != JTokenType.Boolean) {
                throw new InvalidDataException(path + propertyName + " must be a boolean.");
            }
        }
    }

    internal static class HermesV2ManifestValidator {
        private const int MaximumOffset = 16 * 1024 * 1024;
        private static readonly Regex GitSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex Sha256 = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex Revision = new Regex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex Pattern = new Regex("^(?:[0-9A-F]{2}|\\?\\?)+$", RegexOptions.CultureInvariant);
        private static readonly Regex ManifestPath = new Regex("^manifests/sha256:[0-9a-f]{64}\\.json$", RegexOptions.CultureInvariant);
        private static readonly Regex SemanticVersionPattern = new Regex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);

        internal static void Validate(HermesV2Manifest manifest, string clientVersion, bool allowCandidate) {
            if (manifest.SchemaVersion != 2 || manifest.Compatibility == null || manifest.Compatibility.PointerResolverVersion != 1) {
                throw new InvalidDataException("Unsupported Hermes schema or pointer resolver version.");
            }

            if (CompareSemanticVersions(clientVersion, manifest.Compatibility.MinimumSharlayanVersion) < 0) {
                throw new InvalidDataException("Hermes manifest requires a newer Sharlayan version.");
            }

            if (manifest.Source == null
                || manifest.Source.FcsRepository != "https://github.com/aers/FFXIVClientStructs.git"
                || manifest.Source.GeneratorRepository != "https://github.com/sappho192/ffxiv-hermes.git"
                || !GitSha.IsMatch(manifest.Source.FcsCommit ?? string.Empty)
                || !GitSha.IsMatch(manifest.Source.GeneratorCommit ?? string.Empty)) {
                throw new InvalidDataException("Hermes source identity is invalid.");
            }

            if (manifest.Platform == null || manifest.Platform.Process != "ffxiv_dx11.exe" || manifest.Platform.Architecture != "x64") {
                throw new InvalidDataException("Hermes manifest targets an unsupported platform.");
            }

            HermesFrameworkRoot framework = manifest.Roots?.Framework ?? throw new InvalidDataException("Framework root is missing.");
            if (!framework.IsPointer
                || !Pattern.IsMatch(framework.Pattern ?? string.Empty)
                || framework.Pattern.Length > 1024
                || framework.RelativeFollowOffset < 0
                || framework.RelativeFollowOffset > framework.Pattern.Length / 2) {
                throw new InvalidDataException("Framework root is invalid.");
            }

            HermesChatLogResource chat = manifest.Resources?.ChatLog ?? throw new InvalidDataException("CHATLOG resource is missing.");
            HermesTalkResource talk = manifest.Resources?.Talk ?? throw new InvalidDataException("Talk resource is missing.");
            if (chat.Root != "framework" || talk.Root != "framework" || talk.Semantics != "lastStandardTalk") {
                throw new InvalidDataException("Hermes resource semantics are invalid.");
            }

            ValidateOffset(chat.UiModuleOffset, nameof(chat.UiModuleOffset));
            ValidateOffset(chat.RaptureLogModuleOffset, nameof(chat.RaptureLogModuleOffset));
            ValidateOffset(chat.IndexVectorOffset, nameof(chat.IndexVectorOffset));
            ValidateOffset(chat.DataVectorOffset, nameof(chat.DataVectorOffset));
            ValidateOffset(talk.UiModuleOffset, nameof(talk.UiModuleOffset));
            ValidateOffset(talk.NameOffset, nameof(talk.NameOffset));
            ValidateOffset(talk.TextOffset, nameof(talk.TextOffset));
            if (chat.UiModuleOffset != talk.UiModuleOffset
                || chat.IndexVectorOffset % 8 != 0
                || chat.DataVectorOffset % 8 != 0
                || Math.Abs(chat.DataVectorOffset - chat.IndexVectorOffset) < 24) {
                throw new InvalidDataException("CHATLOG vector or shared UIModule layout is invalid.");
            }

            HermesUtf8StringLayout utf8 = talk.Utf8String ?? throw new InvalidDataException("Utf8String layout is missing.");
            ValidateOffset(utf8.StringPointerOffset, nameof(utf8.StringPointerOffset));
            ValidateOffset(utf8.BufferUsedOffset, nameof(utf8.BufferUsedOffset));
            ValidateOffset(utf8.StringLengthOffset, nameof(utf8.StringLengthOffset));
            if (utf8.StringPointerOffset % 8 != 0
                || utf8.BufferUsedOffset % 8 != 0
                || utf8.StringLengthOffset % 8 != 0
                || !(utf8.StringPointerOffset < utf8.BufferUsedOffset && utf8.BufferUsedOffset < utf8.StringLengthOffset)) {
                throw new InvalidDataException("Utf8String layout is invalid.");
            }

            HermesValidation validation = manifest.Validation ?? throw new InvalidDataException("Validation metadata is missing.");
            if (validation.Status == "live-verified") {
                if (string.IsNullOrWhiteSpace(validation.GameVersion)
                    || validation.GameVersion.Length > 64
                    || !Sha256.IsMatch(validation.ExecutableSha256 ?? string.Empty)
                    || !GitSha.IsMatch(validation.VerifierCommit ?? string.Empty)) {
                    throw new InvalidDataException("Live verification metadata is incomplete.");
                }
            }
            else if (!(allowCandidate && validation.Status == "candidate")) {
                throw new InvalidDataException("Only live-verified remote and cache manifests are accepted.");
            }
        }

        internal static void ValidateLatest(HermesLatestPointer latest) {
            if (latest.SchemaVersion != 2
                || !Revision.IsMatch(latest.ResourceRevision ?? string.Empty)
                || !ManifestPath.IsMatch(latest.Manifest ?? string.Empty)
                || latest.Manifest != "manifests/" + latest.ResourceRevision + ".json"
                || !GitSha.IsMatch(latest.FcsCommit ?? string.Empty)
                || !DateTimeOffset.TryParse(latest.PublishedAt, out _)) {
                throw new InvalidDataException("Hermes latest pointer is invalid.");
            }
        }

        internal static int CompareSemanticVersions(string left, string right) {
            SemanticVersion leftVersion = SemanticVersion.Parse(left);
            SemanticVersion rightVersion = SemanticVersion.Parse(right);
            return leftVersion.CompareTo(rightVersion);
        }

        private static void ValidateOffset(int value, string name) {
            if (value < 0 || value > MaximumOffset) {
                throw new InvalidDataException($"Hermes offset {name} is out of range.");
            }
        }

        private sealed class SemanticVersion : IComparable<SemanticVersion> {
            private SemanticVersion(string major, string minor, string patch, string[] prerelease) {
                this.Major = major;
                this.Minor = minor;
                this.Patch = patch;
                this.Prerelease = prerelease;
            }

            private string Major { get; }
            private string Minor { get; }
            private string Patch { get; }
            private string[] Prerelease { get; }

            internal static SemanticVersion Parse(string value) {
                if (string.IsNullOrWhiteSpace(value) || !SemanticVersionPattern.IsMatch(value)) {
                    throw new InvalidDataException("Semantic version is invalid.");
                }

                string withoutBuild = value.Split('+')[0];
                string[] releaseParts = withoutBuild.Split(new[] { '-' }, 2);
                string[] numbers = releaseParts[0].Split('.');
                if (numbers.Length != 3) {
                    throw new InvalidDataException("Semantic version is invalid.");
                }

                string[] prerelease = releaseParts.Length == 2 ? releaseParts[1].Split('.') : Array.Empty<string>();
                if (Array.Exists(prerelease, part => part.Length > 1 && part[0] == '0' && IsNumericIdentifier(part))) {
                    throw new InvalidDataException("Semantic version numeric prerelease identifiers must not contain leading zeroes.");
                }

                return new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease);
            }

            public int CompareTo(SemanticVersion other) {
                int result = CompareNumericIdentifier(this.Major, other.Major);
                if (result != 0) return result;
                result = CompareNumericIdentifier(this.Minor, other.Minor);
                if (result != 0) return result;
                result = CompareNumericIdentifier(this.Patch, other.Patch);
                if (result != 0) return result;
                if (this.Prerelease.Length == 0) return other.Prerelease.Length == 0 ? 0 : 1;
                if (other.Prerelease.Length == 0) return -1;
                int count = Math.Min(this.Prerelease.Length, other.Prerelease.Length);
                for (int index = 0; index < count; index++) {
                    bool leftNumeric = IsNumericIdentifier(this.Prerelease[index]);
                    bool rightNumeric = IsNumericIdentifier(other.Prerelease[index]);
                    if (leftNumeric && rightNumeric) result = CompareNumericIdentifier(this.Prerelease[index], other.Prerelease[index]);
                    else if (leftNumeric != rightNumeric) result = leftNumeric ? -1 : 1;
                    else result = string.CompareOrdinal(this.Prerelease[index], other.Prerelease[index]);
                    if (result != 0) return result;
                }

                return this.Prerelease.Length.CompareTo(other.Prerelease.Length);
            }

            private static bool IsNumericIdentifier(string value) {
                for (int index = 0; index < value.Length; index++) {
                    if (value[index] < '0' || value[index] > '9') return false;
                }

                return value.Length > 0;
            }

            private static int CompareNumericIdentifier(string left, string right) {
                int lengthComparison = left.Length.CompareTo(right.Length);
                return lengthComparison != 0 ? lengthComparison : string.CompareOrdinal(left, right);
            }
        }
    }
}
