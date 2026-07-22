namespace Sharlayan.Tests.Utilities {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Sharlayan.Core;
    using Sharlayan.Models;
    using Sharlayan.Models.ReadResults;
    using Sharlayan.Utilities;

    using Xunit;

    public class ChatLogReaderTests {
        private const long IndexStart = 0x2000;

        private const long LogStart = 0x3000;

        [Fact]
        public void EnsureArrayIndexes_ReadsExactIndexBufferOnce() {
            int readCount = 0;
            IntPtr readAddress = IntPtr.Zero;
            int readLength = 0;
            ChatLogReader reader = CreateReader(
                readBytes: (address, destination) => {
                    readCount++;
                    readAddress = address;
                    readLength = destination.Length;
                    for (int i = 0; i < 1000; i++) {
                        WriteInt32(destination, i, i * 10);
                    }
                });
            reader.ChatLogPointers = CreatePointers(currentArrayIndex: 0);

            reader.EnsureArrayIndexes();

            Assert.Equal(1, readCount);
            Assert.Equal(new IntPtr(IndexStart), readAddress);
            Assert.Equal(4000, readLength);
            Assert.Equal(1000, reader.Indexes.Count);
            Assert.Equal(0, reader.Indexes[0]);
            Assert.Equal(9990, reader.Indexes[999]);
        }

        [Fact]
        public void ResolveEntries_ReadsDirectlyIntoExactResultBuffer() {
            IntPtr readAddress = IntPtr.Zero;
            int readLength = 0;
            ChatLogReader reader = CreateReader(
                readBytes: (address, destination) => {
                    readAddress = address;
                    readLength = destination.Length;
                    destination[0] = 0x11;
                    destination[1] = 0x22;
                    destination[2] = 0x33;
                });
            reader.ChatLogPointers = CreatePointers(currentArrayIndex: 0);
            reader.PreviousOffset = 2;
            reader.Indexes.Add(5);

            byte[] entry = reader.ResolveEntries(0, 1).Single();

            Assert.Equal(new IntPtr(LogStart + 2), readAddress);
            Assert.Equal(3, readLength);
            Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, entry);
            Assert.Equal(5, reader.PreviousOffset);
        }

        [Fact]
        public void GetChatLog_FirstPollPrimesCursorWithoutReturningHistory() {
            int indexReadCount = 0;
            ChatLogPointers pointers = CreatePointers(currentArrayIndex: 2);
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => pointers,
                readBytes: (address, destination) => {
                    Assert.Equal(new IntPtr(IndexStart), address);
                    indexReadCount++;
                    WriteInt32(destination, 0, 12);
                    WriteInt32(destination, 1, 34);
                });
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog();

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(1, result.PreviousArrayIndex);
            Assert.Equal(34, result.PreviousOffset);
            Assert.Equal(1, indexReadCount);
        }

        [Fact]
        public void GetChatLog_WrapReturnsTailBeforeHeadAndReadsIndexesOnce() {
            byte[] tailOne = CreateEntry("Tail one");
            byte[] tailTwo = CreateEntry("Tail two");
            byte[] head = CreateEntry("Head");
            int tailStart = 10;
            int tailTwoStart = tailStart + tailOne.Length;
            int indexReadCount = 0;
            Dictionary<long, byte[]> entries = new Dictionary<long, byte[]> {
                [LogStart + tailStart] = tailOne,
                [LogStart + tailTwoStart] = tailTwo,
                [LogStart] = head,
            };
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex: 1),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        indexReadCount++;
                        WriteInt32(destination, 0, head.Length);
                        WriteInt32(destination, 998, tailTwoStart);
                        WriteInt32(destination, 999, tailTwoStart + tailTwo.Length);
                        return;
                    }

                    byte[] source = entries[address.ToInt64()];
                    Assert.Equal(source.Length, destination.Length);
                    Buffer.BlockCopy(source, 0, destination, 0, source.Length);
                });
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog(998, tailStart);

            Assert.Collection(
                result.ChatLogItems,
                item => Assert.Equal("Tail one", item.Message),
                item => Assert.Equal("Tail two", item.Message),
                item => Assert.Equal("Head", item.Message));
            Assert.Equal(1, result.PreviousArrayIndex);
            Assert.Equal(head.Length, result.PreviousOffset);
            Assert.Equal(1, indexReadCount);
        }

        [Fact]
        public async Task GetChatLog_ConcurrentCallsDoNotOverlapPointerReads() {
            int activeReads = 0;
            int maximumActiveReads = 0;
            using ManualResetEventSlim start = new ManualResetEventSlim();
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => {
                    int active = Interlocked.Increment(ref activeReads);
                    UpdateMaximum(ref maximumActiveReads, active);
                    try {
                        Thread.Sleep(25);
                        return CreatePointers(currentArrayIndex: 0);
                    }
                    finally {
                        Interlocked.Decrement(ref activeReads);
                    }
                });
            Reader reader = new Reader(chatLogReader);

            Task[] calls = Enumerable.Range(0, 8)
                .Select(
                    _ => Task.Run(
                        () => {
                            start.Wait();
                            reader.GetChatLog();
                        }))
                .ToArray();
            start.Set();

            Task allCalls = Task.WhenAll(calls);
            Assert.Same(allCalls, await Task.WhenAny(allCalls, Task.Delay(TimeSpan.FromSeconds(5))));
            await allCalls;
            Assert.Equal(1, maximumActiveReads);
        }

        private static ChatLogReader CreateReader(
            Func<IntPtr, ChatLogPointers> readPointers = null,
            Action<IntPtr, byte[]> readBytes = null) {
            return new ChatLogReader(
                () => true,
                () => true,
                () => new IntPtr(0x1000),
                readPointers ?? (_ => CreatePointers(currentArrayIndex: 0)),
                readBytes ?? ((_, _) => { }),
                (_, _) => { });
        }

        private static ChatLogPointers CreatePointers(int currentArrayIndex) {
            return new ChatLogPointers {
                OffsetArrayStart = IndexStart,
                OffsetArrayPos = IndexStart + currentArrayIndex * sizeof(int),
                OffsetArrayEnd = IndexStart + 4000,
                LogStart = LogStart,
            };
        }

        private static byte[] CreateEntry(string message) {
            byte[] body = Encoding.UTF8.GetBytes(message);
            byte[] result = new byte[9 + body.Length];
            result[0] = 0x04;
            result[1] = 0x03;
            result[2] = 0x02;
            result[3] = 0x01;
            result[4] = 0x34;
            result[5] = 0x12;
            result[8] = 0x1F;
            Buffer.BlockCopy(body, 0, result, 9, body.Length);
            return result;
        }

        private static void UpdateMaximum(ref int maximum, int value) {
            int observed;
            do {
                observed = maximum;
                if (observed >= value) {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref maximum, value, observed) != observed);
        }

        private static void WriteInt32(byte[] destination, int index, int value) {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, destination, index * sizeof(int), sizeof(int));
        }
    }
}
