namespace Sharlayan.Tests.Utilities {
    using System.Collections.Generic;
    using System.Text;

    using Sharlayan.Utilities;

    using Xunit;

    public class ChatCleanerTests {
        public static IEnumerable<object[]> TruncatedPayloads() {
            yield return new object[] { new byte[] { 0xEE, 0x80, 0xBC, 0x02 } };
            yield return new object[] { new byte[] { 0xEE, 0x80, 0xBC, 0x02, 0x1D, 0x01, 0x03 } };
        }

        [Theory]
        [MemberData(nameof(TruncatedPayloads))]
        public void ProcessFullLine_PreservesTextBeforeTruncatedPayload(byte[] bytes) {
            string actual = ChatCleaner.ProcessFullLine("1234", bytes);

            Assert.Equal("[HQ]", actual);
        }

        [Fact]
        public void ProcessFullLine_DecodesHtmlEntities() {
            byte[] bytes = Encoding.UTF8.GetBytes("A&amp;B");

            string actual = ChatCleaner.ProcessFullLine("1234", bytes);

            Assert.Equal("A&B", actual);
        }

        [Fact]
        public void ProcessFullLine_RemovesControlCharactersAndNewLines() {
            byte[] bytes = { 0x41, 0x0D, 0x0A, 0x00, 0x09, 0x42 };

            string actual = ChatCleaner.ProcessFullLine("1234", bytes);

            Assert.Equal("AB", actual);
        }
    }
}
