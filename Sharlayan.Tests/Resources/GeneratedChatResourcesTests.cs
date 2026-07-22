namespace Sharlayan.Tests.Resources {
    using System.Reflection;
    using System.Threading.Tasks;

    using Sharlayan.Resources;
    using Sharlayan.Utilities;

    using Xunit;

    using Signature = Sharlayan.Models.Signature;
    using ChatLogPointersStructure = Sharlayan.Models.Structures.ChatLogPointers;
    using StructuresContainer = Sharlayan.Models.Structures.StructuresContainer;

    public class GeneratedChatResourcesTests {
        [Fact]
        public void CreateSignatures_ReturnsPinnedFcsChatSignature() {
            Signature signature = Assert.Single(GeneratedChatResources.CreateSignatures());

            Assert.Equal("15ae1806b0c175d1e2dd2ae845e4c853f332fd07", GeneratedChatResources.FcsCommit);
            Assert.Equal(Signatures.CHATLOG_KEY, signature.Key);
            Assert.Equal("488B1D????????8B7C24", signature.Value);
            Assert.True(signature.ASMSignature);
            Assert.Equal(new long[] { -7, 0, 0x2B68, 0x1AC0 }, signature.PointerPath);
        }

        [Fact]
        public void CreateStructures_ReturnsLogModuleVectorOffsets() {
            ChatLogPointersStructure pointers = GeneratedChatResources.CreateStructures().ChatLogPointers;

            Assert.Equal(0x48, pointers.OffsetArrayStart);
            Assert.Equal(0x50, pointers.OffsetArrayPos);
            Assert.Equal(0x58, pointers.OffsetArrayEnd);
            Assert.Equal(0x60, pointers.LogStart);
            Assert.Equal(0x68, pointers.LogNext);
            Assert.Equal(0x70, pointers.LogEnd);
        }

        [Fact]
        public async Task Resolve_ReturnsIndependentGeneratedResources() {
            Signature[] first = await Signatures.Resolve(new SharlayanConfiguration());
            first[0].Value = string.Empty;

            Signature[] second = await Signatures.Resolve(new SharlayanConfiguration());

            Assert.Equal("488B1D????????8B7C24", second[0].Value);
        }

        [Fact]
        public async Task ApiHelperCompatibilityMethodsReturnGeneratedResources() {
#pragma warning disable CS0618
            Signature[] signatures = await APIHelper.GetSignatures(new SharlayanConfiguration());
            StructuresContainer structures = await APIHelper.GetStructures(new SharlayanConfiguration());
#pragma warning restore CS0618

            Assert.Single(signatures);
            Assert.Equal(0x48, structures.ChatLogPointers.OffsetArrayStart);
        }

        [Fact]
        public void RuntimeAssemblyDoesNotReferenceGeneratorDependencies() {
            AssemblyName[] references = typeof(MemoryHandler).Assembly.GetReferencedAssemblies();

            Assert.DoesNotContain(references, reference => reference.Name == "FFXIVClientStructs");
            Assert.DoesNotContain(references, reference => reference.Name == "InteropGenerator.Runtime");
            Assert.DoesNotContain(references, reference => reference.Name == "Lumina");
            Assert.DoesNotContain(references, reference => reference.Name == "System.Net.Http");
        }
    }
}
