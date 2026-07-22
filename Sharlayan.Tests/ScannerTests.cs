namespace Sharlayan.Tests {
    using Xunit;

    public class ScannerTests {
        private const byte Wildcard = (byte) '?';

        [Fact]
        public void SignatureToByte_ConvertsHexAndWildcardBytes() {
            byte[] actual = Scanner.SignatureToByte("48??a0", Wildcard);

            Assert.Equal(new byte[] { 0x48, Wildcard, 0xA0 }, actual);
        }

        [Fact]
        public void FindSuperSignature_FindsExactPattern() {
            byte[] buffer = { 0x00, 0x48, 0x8B, 0x01, 0xFF };
            byte[] pattern = { 0x48, 0x8B, 0x01 };

            int actual = Scanner.FindSuperSignature(buffer, pattern);

            Assert.Equal(1, actual);
        }

        [Fact]
        public void FindSuperSignature_MatchesWildcardByte() {
            byte[] buffer = { 0x48, 0x8B, 0x7F, 0xFF };
            byte[] pattern = { 0x48, Wildcard, 0x7F };

            int actual = Scanner.FindSuperSignature(buffer, pattern);

            Assert.Equal(0, actual);
        }

        [Fact]
        public void FindSuperSignature_ReturnsMinusOneWhenPatternIsAbsent() {
            byte[] buffer = { 0x00, 0x01, 0x02 };

            Assert.Equal(-1, Scanner.FindSuperSignature(buffer, new byte[] { 0x03, 0x04 }));
            Assert.Equal(-1, Scanner.FindSuperSignature(buffer, new byte[] { 0x00, 0x01, 0x02, 0x03 }));
        }
    }
}
