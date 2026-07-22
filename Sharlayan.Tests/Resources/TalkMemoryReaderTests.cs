namespace Sharlayan.Tests.Resources {
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Sharlayan.Models.ReadResults;
    using Sharlayan.Resources;

    using Xunit;

    public sealed class TalkMemoryReaderTests {
        [Fact]
        public void ReadsUtf8NameAndTextFromStablePairSnapshot() {
            SyntheticMemory memory = new SyntheticMemory();
            memory.AddUtf8String(0x1000, 0x3000, "에르메스");
            memory.AddUtf8String(0x2000, 0x4000, "星の海へ行こう。");

            TalkResult result = TalkMemoryReader.Read(memory.Peek, new IntPtr(0x1000), new IntPtr(0x2000), new TalkMemoryLayout(0, 0x10, 0x18));

            Assert.True(result.IsAvailable);
            Assert.Equal("에르메스", result.Name);
            Assert.Equal("星の海へ行こう。", result.Text);
        }

        [Fact]
        public void RejectsInvalidUtf8AndOversizedLength() {
            SyntheticMemory invalid = new SyntheticMemory();
            invalid.AddRawUtf8String(0x1000, 0x3000, new byte[] { 0xC3, 0x28 }, 2);
            invalid.AddUtf8String(0x2000, 0x4000, "ok");
            SyntheticMemory oversized = new SyntheticMemory();
            oversized.AddRawUtf8String(0x1000, 0x3000, Array.Empty<byte>(), TalkMemoryReader.MaximumStringBytes + 1);
            oversized.AddUtf8String(0x2000, 0x4000, "ok");

            Assert.False(TalkMemoryReader.Read(invalid.Peek, new IntPtr(0x1000), new IntPtr(0x2000), new TalkMemoryLayout(0, 0x10, 0x18)).IsAvailable);
            Assert.False(TalkMemoryReader.Read(oversized.Peek, new IntPtr(0x1000), new IntPtr(0x2000), new TalkMemoryLayout(0, 0x10, 0x18)).IsAvailable);
        }

        [Fact]
        public void RetriesOnceWhenHeaderChangesDuringRead() {
            SyntheticMemory memory = new SyntheticMemory();
            memory.AddUtf8String(0x1000, 0x3000, "first");
            memory.AddUtf8String(0x2000, 0x4000, "text");
            int nameHeaderReads = 0;
            bool Peek(IntPtr address, byte[] buffer, int count) {
                if (address == new IntPtr(0x1000) && ++nameHeaderReads == 2) {
                    memory.AddUtf8String(0x1000, 0x5000, "second");
                }
                return memory.Peek(address, buffer, count);
            }

            TalkResult result = TalkMemoryReader.Read(Peek, new IntPtr(0x1000), new IntPtr(0x2000), new TalkMemoryLayout(0, 0x10, 0x18));

            Assert.True(result.IsAvailable);
            Assert.Equal("second", result.Name);
        }

        private sealed class SyntheticMemory {
            private readonly Dictionary<long, byte[]> _memory = new Dictionary<long, byte[]>();

            internal void AddUtf8String(long headerAddress, long dataAddress, string value) {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                this.AddRawUtf8String(headerAddress, dataAddress, bytes, bytes.Length);
            }

            internal void AddRawUtf8String(long headerAddress, long dataAddress, byte[] bytes, int declaredLength) {
                byte[] header = new byte[0x20];
                Buffer.BlockCopy(BitConverter.GetBytes(dataAddress), 0, header, 0, 8);
                Buffer.BlockCopy(BitConverter.GetBytes((long)declaredLength + 1), 0, header, 0x10, 8);
                Buffer.BlockCopy(BitConverter.GetBytes((long)declaredLength), 0, header, 0x18, 8);
                byte[] terminated = new byte[bytes.Length + 1];
                Buffer.BlockCopy(bytes, 0, terminated, 0, bytes.Length);
                this._memory[headerAddress] = header;
                this._memory[dataAddress] = terminated;
            }

            internal bool Peek(IntPtr address, byte[] buffer, int count) {
                if (!this._memory.TryGetValue(address.ToInt64(), out byte[] source) || count > source.Length) return false;
                Buffer.BlockCopy(source, 0, buffer, 0, count);
                return true;
            }
        }
    }
}
