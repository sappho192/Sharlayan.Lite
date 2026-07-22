namespace Sharlayan.Tests.Resources {
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Newtonsoft.Json.Linq;

    using Sharlayan.Models.Resources;
    using Sharlayan.Resources;

    using Xunit;

    public sealed class HermesV2ManifestTests {
        [Fact]
        public void EmbeddedManifestParsesAndMatchesExpectedRevision() {
            byte[] bytes = ReadEmbeddedFixture();

            HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(bytes, null, "9.1.2", allowCandidate: true);

            Assert.Equal(2, manifest.SchemaVersion);
            Assert.Equal("15ae1806b0c175d1e2dd2ae845e4c853f332fd07", manifest.Source.FcsCommit);
            Assert.Equal("sha256:fe5330592a8e4fcf1912b76b28d9338571356185a8888ae91b97fff497c6f994", HermesV2ManifestParser.CalculateRevision(bytes));
        }

        [Fact]
        public void RemoteParserRejectsCandidateValidation() {
            byte[] bytes = ReadEmbeddedFixture();

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(bytes, null, "9.1.2", allowCandidate: false));
        }

        [Fact]
        public void ParserRejectsRevisionMismatchAndNewerMinimumVersion() {
            byte[] bytes = CreateLiveManifest("99.0.0");

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(bytes, "sha256:" + new string('0', 64), "9.1.2", allowCandidate: false));
            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(bytes, null, "9.1.2", allowCandidate: false));
        }

        [Fact]
        public void ParserRejectsTrailingJsonAndLatestPathTraversal() {
            byte[] trailing = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ReadEmbeddedFixture()) + "{}");
            byte[] latest = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"resourceRevision\":\"sha256:" + new string('a', 64) + "\",\"manifest\":\"../manifest.json\",\"fcsCommit\":\"" + new string('b', 40) + "\",\"publishedAt\":\"2026-07-22T00:00:00Z\"}");

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(trailing, null, "9.1.2", allowCandidate: true));
            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseLatest(latest));
        }

        [Fact]
        public void ParserRejectsKnownFieldTypeMismatchAndInvalidSemver() {
            JObject wrongType = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            wrongType["schemaVersion"] = "2";
            JObject invalidSemver = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            invalidSemver["compatibility"]["minimumSharlayanVersion"] = "9.1.2+invalid!";

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(wrongType.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.1.2",
                allowCandidate: true));
            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(invalidSemver.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.1.2",
                allowCandidate: true));
        }

        [Fact]
        public void ParserRejectsCandidateWithLiveVerificationFields() {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            json["validation"]["gameVersion"] = "7.51";

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.1.2",
                allowCandidate: true));
        }

        [Fact]
        public void ParserIgnoresTypedOptionalResources() {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            json["resources"]["talkSubtitle"] = new JObject { ["future"] = true };
            byte[] bytes = Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));

            HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(bytes, null, "9.1.2", allowCandidate: true);

            Assert.True(manifest.Resources.OptionalResources.ContainsKey("talkSubtitle"));
        }

        [Fact]
        public void MapperKeepsChatAndTalkOnOneManifest() {
            HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(ReadEmbeddedFixture(), null, "9.1.2", allowCandidate: true);

            HermesMappedResources mapped = HermesV2ResourceMapper.Map(manifest);

            Assert.Equal(3, mapped.Signatures.Length);
            Assert.Equal(new[] { Signatures.CHATLOG_KEY, Signatures.LAST_TALK_NAME_KEY, Signatures.LAST_TALK_TEXT_KEY }, mapped.Signatures.Select(signature => signature.Key));
            Assert.All(mapped.Signatures, signature => Assert.Equal("488B1D????????8B7C24", signature.Value));
            Assert.Equal(new long[] { -7, 0, 0x2B68, 0x1AC0 }, mapped.Signatures[0].PointerPath);
            Assert.Equal(new long[] { -7, 0, 0x2B68, 0xFEF00 }, mapped.Signatures[1].PointerPath);
            Assert.Equal(0x48, mapped.Structures.ChatLogPointers.OffsetArrayStart);
            Assert.Equal(0x70, mapped.Structures.ChatLogPointers.LogEnd);
            Assert.Equal(0x18, mapped.TalkLayout.StringLengthOffset);
        }

        internal static byte[] CreateLiveManifest(string minimumVersion = "9.1.2") {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            json["compatibility"]["minimumSharlayanVersion"] = minimumVersion;
            json["validation"] = new JObject {
                ["status"] = "live-verified",
                ["gameVersion"] = "7.51",
                ["executableSha256"] = new string('c', 64),
                ["verifierCommit"] = new string('d', 40),
            };
            return new UTF8Encoding(false).GetBytes(json.ToString(Newtonsoft.Json.Formatting.Indented) + "\n");
        }

        private static byte[] ReadEmbeddedFixture() {
            using (Stream stream = typeof(HermesV2ResourceProvider).Assembly.GetManifestResourceStream("Sharlayan.Resources.HermesV2.embedded.json"))
            using (MemoryStream output = new MemoryStream()) {
                Assert.NotNull(stream);
                stream.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
