namespace Sharlayan.Tests {
    using System;
    using System.Text;

    using Xunit;

    public class MemoryHandlerTests {
        [Fact]
        public void DecodeString_StopsAtNullTerminator() {
            byte[] source = { 0x41, 0x42, 0x00, 0x43 };

            string actual = MemoryHandler.DecodeString(source, 0, source.Length);

            Assert.Equal("AB", actual);
        }

        [Fact]
        public void DecodeString_ReturnsEntireRangeWithoutNullTerminator() {
            byte[] source = Encoding.UTF8.GetBytes("hello");

            string actual = MemoryHandler.DecodeString(source, 0, source.Length);

            Assert.Equal("hello", actual);
        }

        [Fact]
        public void DecodeString_RespectsOffsetAndAvailableLength() {
            byte[] source = Encoding.UTF8.GetBytes("012345");

            string actual = MemoryHandler.DecodeString(source, 2, 10);

            Assert.Equal("2345", actual);
        }

        [Fact]
        public void DecodeString_RejectsInvalidBounds() {
            Assert.Throws<ArgumentNullException>(() => MemoryHandler.DecodeString(null, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemoryHandler.DecodeString(Array.Empty<byte>(), -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemoryHandler.DecodeString(Array.Empty<byte>(), 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemoryHandler.DecodeString(Array.Empty<byte>(), 0, -1));
        }
    }
}
