namespace Sharlayan.Tests.Utilities {
    using System;
    using System.Text;

    using Sharlayan.Core;
    using Sharlayan.Utilities;

    using Xunit;

    public class ChatEntryTests {
        private const int TimestampSeconds = 0x01020304;

        [Fact]
        public void Process_ParsesHeaderAndMessage() {
            byte[] raw = CreateEntry(0x34, 0x12, 0x1F, 0x48, 0x65, 0x6C, 0x6C, 0x6F);

            ChatLogItem actual = ChatEntry.Process(raw);

            Assert.Same(raw, actual.Bytes);
            Assert.Equal(ExpectedTimestamp(), actual.TimeStamp);
            Assert.Equal("1234", actual.Code);
            Assert.Equal(Encoding.UTF8.GetString(raw), actual.Raw);
            Assert.Equal("Hello", actual.Line);
            Assert.Equal("Hello", actual.Message);
            Assert.Equal("1234:Hello", actual.Combined);
            Assert.Null(actual.PlayerName);
            Assert.False(actual.IsInternational);
        }

        [Fact]
        public void Process_SeparatesPublicChatSenderAndMessage() {
            byte[] raw = CreateEntry(
                0x0A,
                0x00,
                0x1F,
                0x41, 0x6C, 0x69, 0x63, 0x65,
                0x20,
                0x45, 0x78, 0x61, 0x6D, 0x70, 0x6C, 0x65,
                0x1F,
                0x48, 0x65, 0x6C, 0x6C, 0x6F);

            ChatLogItem actual = ChatEntry.Process(raw);

            Assert.Equal("000A", actual.Code);
            Assert.Equal("Alice Example: Hello", actual.Line);
            Assert.Equal("Alice Example", actual.PlayerName);
            Assert.Equal("Hello", actual.Message);
            Assert.Equal("000A:Alice Example: Hello", actual.Combined);
        }

        [Fact]
        public void Process_LeavesColonFreePublicChatUnsplit() {
            byte[] raw = CreateEntry(0x0A, 0x00, 0x1F, 0x48, 0x65, 0x6C, 0x6C, 0x6F);

            ChatLogItem actual = ChatEntry.Process(raw);

            Assert.Equal("Hello", actual.Line);
            Assert.Equal("Hello", actual.Message);
            Assert.Null(actual.PlayerName);
            Assert.Equal("000A:Hello", actual.Combined);
        }

        [Fact]
        public void Process_HandlesEntryWithoutPayload() {
            byte[] raw = { 0x04, 0x03, 0x02, 0x01, 0x34, 0x12, 0x00 };

            ChatLogItem actual = ChatEntry.Process(raw);

            Assert.Same(raw, actual.Bytes);
            Assert.Equal(ExpectedTimestamp(), actual.TimeStamp);
            Assert.Equal("1234", actual.Code);
            Assert.Equal(Encoding.UTF8.GetString(raw), actual.Raw);
            Assert.Equal(string.Empty, actual.Line);
            Assert.Equal(string.Empty, actual.Message);
            Assert.Equal("1234:", actual.Combined);
        }

        [Fact]
        public void Process_DetectsJapaneseTextAsInternational() {
            byte[] message = Encoding.UTF8.GetBytes("日本語");
            byte[] raw = CreateEntry(0x34, 0x12, PrependSeparator(message));

            ChatLogItem actual = ChatEntry.Process(raw);

            Assert.Equal("日本語", actual.Line);
            Assert.Equal("日本語", actual.Message);
            Assert.Equal("1234:日本語", actual.Combined);
            Assert.True(actual.IsInternational);
        }

        private static byte[] CreateEntry(byte codeLow, byte codeHigh, params byte[] body) {
            byte[] result = new byte[8 + body.Length];
            result[0] = 0x04;
            result[1] = 0x03;
            result[2] = 0x02;
            result[3] = 0x01;
            result[4] = codeLow;
            result[5] = codeHigh;
            Buffer.BlockCopy(body, 0, result, 8, body.Length);
            return result;
        }

        private static DateTime ExpectedTimestamp() {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(TimestampSeconds).ToLocalTime();
        }

        private static byte[] PrependSeparator(byte[] message) {
            byte[] result = new byte[message.Length + 1];
            result[0] = 0x1F;
            Buffer.BlockCopy(message, 0, result, 1, message.Length);
            return result;
        }
    }
}
