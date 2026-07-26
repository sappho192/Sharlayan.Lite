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

            HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(bytes, null, "9.2.0", allowCandidate: true);

            Assert.Equal(2, manifest.SchemaVersion);
            Assert.Equal("ed2cd7049c4d84d9e2ccb3eb55245ea712b040f1", manifest.Source.FcsCommit);
            Assert.Equal("sha256:065e24246f0707101601d76fd23ce159d26f74f60c0368a589ef4da0a7abb701", HermesV2ManifestParser.CalculateRevision(bytes));
        }

        [Fact]
        public void RemoteParserAcceptsLiveVerifiedValidation() {
            byte[] bytes = ReadEmbeddedFixture();

            HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(bytes, null, "9.2.0", allowCandidate: false);

            Assert.Equal("live-verified", manifest.Validation.Status);
            Assert.Equal("36ebee4a4926dd45607bf973c6205b8eec04b480", manifest.Validation.VerifierCommit);
        }

        [Fact]
        public void ParserRejectsRevisionMismatchAndNewerMinimumVersion() {
            byte[] bytes = CreateLiveManifest("99.0.0");

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(bytes, "sha256:" + new string('0', 64), "9.2.0", allowCandidate: false));
            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(bytes, null, "9.2.0", allowCandidate: false));
        }

        [Fact]
        public void ParserRejectsTrailingJsonAndLatestPathTraversal() {
            byte[] trailing = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ReadEmbeddedFixture()) + "{}");
            byte[] latest = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"resourceRevision\":\"sha256:" + new string('a', 64) + "\",\"manifest\":\"../manifest.json\",\"fcsCommit\":\"" + new string('b', 40) + "\",\"publishedAt\":\"2026-07-22T00:00:00Z\"}");

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(trailing, null, "9.2.0", allowCandidate: true));
            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseLatest(latest));
        }

        [Fact]
        public void ParserRejectsKnownFieldTypeMismatchAndInvalidSemver() {
            JObject wrongType = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            wrongType["schemaVersion"] = "2";
            JObject invalidSemver = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            invalidSemver["compatibility"]["minimumSharlayanVersion"] = "9.2.0+invalid!";

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(wrongType.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.2.0",
                allowCandidate: true));
            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(invalidSemver.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.2.0",
                allowCandidate: true));
        }

        [Fact]
        public void ParserRejectsCandidateWithLiveVerificationFields() {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            json["validation"]["status"] = "candidate";

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.2.0",
                allowCandidate: true));
        }

        [Fact]
        public void ParserRejectsInvalidCurrentTalkAndLengthContracts() {
            JObject wrongLengthSource = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            wrongLengthSource["resources"]["talk"]["utf8String"]["lengthSource"] = "stringLength";
            JObject wrongCurrentType = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            wrongCurrentType["resources"]["currentTalk"]["atkValue"]["allowedStringTypes"] = new JArray(3);

            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(wrongLengthSource.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.2.0",
                allowCandidate: true));
            Assert.Throws<InvalidDataException>(() => HermesV2ManifestParser.ParseManifest(
                Encoding.UTF8.GetBytes(wrongCurrentType.ToString(Newtonsoft.Json.Formatting.None)),
                null,
                "9.2.0",
                allowCandidate: true));
        }

        [Fact]
        public void ParserRejectsUnexpectedResources() {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            json["resources"]["talkSubtitle"] = new JObject { ["future"] = true };
            byte[] bytes = Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));

            Assert.Throws<InvalidDataException>(() =>
                HermesV2ManifestParser.ParseManifest(bytes, null, "9.2.0", allowCandidate: true));
        }

        [Fact]
        public void MapperKeepsChatAndTalkOnOneManifest() {
            HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(ReadEmbeddedFixture(), null, "9.2.0", allowCandidate: true);

            HermesMappedResources mapped = HermesV2ResourceMapper.Map(manifest);

            Assert.Equal(4, mapped.Signatures.Length);
            Assert.Equal(
                new[] {
                    Signatures.CHATLOG_KEY,
                    Signatures.LAST_TALK_NAME_KEY,
                    Signatures.LAST_TALK_TEXT_KEY,
                    Signatures.CURRENT_TALK_UI_MODULE_POINTER_KEY,
                },
                mapped.Signatures.Select(signature => signature.Key));
            Assert.All(mapped.Signatures, signature => Assert.Equal("488B1D????????8B7C24", signature.Value));
            Assert.Equal(new long[] { -7, 0, 0x2B68, 0x1AC0 }, mapped.Signatures[0].PointerPath);
            Assert.Equal(new long[] { -7, 0, 0x2B68, 0xFEF00 }, mapped.Signatures[1].PointerPath);
            Assert.Equal(new long[] { -7, 0, 0x2B68 }, mapped.Signatures[3].PointerPath);
            Assert.Equal(0x48, mapped.Structures.ChatLogPointers.OffsetArrayStart);
            Assert.Equal(0x70, mapped.Structures.ChatLogPointers.LogEnd);
            Assert.Equal(0x18, mapped.TalkLayout.HeaderSize);
            Assert.Equal(861808, mapped.CurrentTalkLayout.RaptureAtkModuleOffset);
            Assert.Equal(78880, mapped.CurrentTalkLayout.RaptureAtkUnitManagerOffset);
            Assert.Equal(26880, mapped.CurrentTalkLayout.AllLoadedUnitsListOffset);
            Assert.Equal(0x200000u, mapped.CurrentTalkLayout.VisibilityMask);
            Assert.Equal(new[] { 0x28 }, mapped.CurrentTalkLayout.AllowedStringTypes);
        }

        [Fact]
        public void ParserAndMapperAcceptOptionalBattleTalk() {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            json["compatibility"]["minimumSharlayanVersion"] = "9.2.0";
            json["resources"]["battleTalk"] = new JObject {
                ["root"] = "framework",
                ["semantics"] = "currentBattleTalk",
                ["uiModuleOffset"] = 11112,
                ["raptureAtkModuleOffset"] = 861808,
                ["raptureAtkUnitManagerOffset"] = 78880,
                ["allLoadedUnitsListOffset"] = 26880,
                ["atkUnitList"] = json["resources"]["currentTalk"]["atkUnitList"].DeepClone(),
                ["addon"] = new JObject {
                    ["nameOffset"] = 8,
                    ["nameCapacity"] = 32,
                    ["visibilityStateOffset"] = 408,
                    ["visibilityMask"] = 2097152,
                    ["readinessOffset"] = 417,
                    ["readinessMask"] = 1,
                },
                ["addonName"] = "_BattleTalk",
                ["atkArrayDataHolderOffset"] = 7080,
                ["arrayDataHolder"] = new JObject {
                    ["numberArrayCountOffset"] = 0,
                    ["numberArraysOffset"] = 24,
                    ["stringArrayCountOffset"] = 2,
                    ["stringArraysOffset"] = 48,
                },
                ["arrayData"] = new JObject {
                    ["sizeOffset"] = 8,
                    ["updateStateOffset"] = 31,
                },
                ["numberValuesOffset"] = 40,
                ["stringValuesOffset"] = 40,
                ["numberArrayId"] = 38,
                ["stringArrayId"] = 35,
                ["visibleIndex"] = 0,
                ["nameIndex"] = 0,
                ["textIndex"] = 1,
                ["sequenceSemantics"] = "visibilityOrContentGeneration",
            };
            byte[] bytes = Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));

            HermesV2Manifest manifest =
                HermesV2ManifestParser.ParseManifest(bytes, null, "9.2.0", allowCandidate: true);
            HermesMappedResources mapped = HermesV2ResourceMapper.Map(manifest);

            Assert.NotNull(mapped.BattleTalkLayout);
            Assert.Equal("_BattleTalk", mapped.BattleTalkLayout.AddonName);
            Assert.Equal(38, mapped.BattleTalkLayout.NumberArrayId);
            Assert.Equal(35, mapped.BattleTalkLayout.StringArrayId);
            Assert.Equal(0, mapped.BattleTalkLayout.NameIndex);
            Assert.Equal(1, mapped.BattleTalkLayout.TextIndex);
        }

        internal static byte[] CreateLiveManifest(string minimumVersion = "9.2.0") {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(ReadEmbeddedFixture()));
            json["compatibility"]["minimumSharlayanVersion"] = minimumVersion;
            if (minimumVersion != "9.2.0") {
                json["resources"]["battleTalk"].Parent.Remove();
            }
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
