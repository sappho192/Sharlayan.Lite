namespace Sharlayan.Tests.Utilities {
    using System;
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
        public void EnsureArrayIndexes_ReadsDynamicIndexBufferOnce() {
            int readCount = 0;
            IntPtr readAddress = IntPtr.Zero;
            int readLength = 0;
            ChatLogReader reader = CreateReader(
                readBytes: (address, destination) => {
                    readCount++;
                    readAddress = address;
                    readLength = destination.Length;
                    for (int i = 0; i < 3; i++) {
                        WriteInt32(destination, i, i * 10);
                    }
                });
            reader.ChatLogPointers = CreatePointers(currentArrayIndex: 3, capacity: 3);

            int capacity = reader.GetArrayCapacity(out int currentArrayIndex);
            reader.EnsureArrayIndexes(currentArrayIndex);

            Assert.Equal(1, readCount);
            Assert.Equal(new IntPtr(IndexStart), readAddress);
            Assert.Equal(12, readLength);
            Assert.Equal(3, reader.Indexes.Count);
            Assert.Equal(3, currentArrayIndex);
            Assert.Equal(0, reader.Indexes[0]);
            Assert.Equal(20, reader.Indexes[2]);
        }

        [Fact]
        public void EnsureArrayIndexes_EmptyVectorDoesNotReadCapacityTail() {
            int readCount = 0;
            ChatLogReader reader = CreateReader(readBytes: (_, _) => readCount++);
            reader.ChatLogPointers = CreatePointers(currentArrayIndex: 0, capacity: 1066);

            reader.EnsureArrayIndexes(0);

            Assert.Empty(reader.Indexes);
            Assert.Equal(0, readCount);
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
            Assert.Equal(2, result.PreviousArrayIndex);
            Assert.Equal(34, result.PreviousOffset);
            Assert.Equal(1, indexReadCount);
        }

        [Fact]
        public void GetChatLog_FirstPollAtZeroPrimesCursorAndNextPollReturnsFirstEntry() {
            byte[] entry = CreateEntry("First entry");
            int currentArrayIndex = 0;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex, capacity: 3),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        if (currentArrayIndex > 0) {
                            WriteInt32(destination, 0, entry.Length);
                        }
                    }
                    else {
                        Buffer.BlockCopy(entry, 0, destination, 0, entry.Length);
                    }
                });
            Reader reader = new Reader(chatLogReader);

            ChatLogResult first = reader.GetChatLog();
            currentArrayIndex = 1;
            ChatLogResult second = reader.GetChatLog(first.PreviousArrayIndex, first.PreviousOffset);

            Assert.Empty(first.ChatLogItems);
            Assert.Equal(0, first.PreviousArrayIndex);
            Assert.Equal("First entry", second.ChatLogItems.Single().Message);
        }

        [Fact]
        public void GetChatLog_CountResetAfterFirstPollReprimesCurrentEnd() {
            int currentArrayIndex = 2;
            int entryReadCount = 0;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex, capacity: 3),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, 0, currentArrayIndex == 1 ? 7 : 10);
                        if (currentArrayIndex == 2) {
                            WriteInt32(destination, 1, 20);
                        }

                        return;
                    }

                    entryReadCount++;
                });
            Reader reader = new Reader(chatLogReader);

            ChatLogResult first = reader.GetChatLog();
            currentArrayIndex = 1;
            ChatLogResult second = reader.GetChatLog(first.PreviousArrayIndex, first.PreviousOffset);

            Assert.Empty(first.ChatLogItems);
            Assert.Equal(2, first.PreviousArrayIndex);
            Assert.Empty(second.ChatLogItems);
            Assert.Equal(1, second.PreviousArrayIndex);
            Assert.Equal(7, second.PreviousOffset);
            Assert.Equal(0, entryReadCount);
        }

        [Fact]
        public void GetChatLog_CountResetDoesNotReadStaleCapacityTail() {
            int indexReadCount = 0;
            int entryReadCount = 0;
            Exception raisedException = null;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex: 173, capacity: 1066),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        indexReadCount++;
                        for (int i = 0; i < 173; i++) {
                            WriteInt32(destination, i, (i + 1) * 10);
                        }

                        return;
                    }

                    entryReadCount++;
                },
                raiseException: exception => raisedException = exception);
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog(previousArrayIndex: 290, previousOffset: 19973);

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(173, result.PreviousArrayIndex);
            Assert.Equal(1730, result.PreviousOffset);
            Assert.Equal(1, indexReadCount);
            Assert.Equal(0, entryReadCount);
            Assert.Null(raisedException);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1048577)]
        public void GetChatLog_InvalidEntrySizeReturnsEmptyResultAndRaisesException(int entryEnd) {
            Exception raisedException = null;
            int entryReadCount = 0;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex: 1, capacity: 3),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, 0, entryEnd);
                    }
                    else {
                        entryReadCount++;
                    }
                },
                raiseException: exception => raisedException = exception);
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog();

            Assert.Empty(result.ChatLogItems);
            Assert.IsType<InvalidOperationException>(raisedException);
            Assert.Equal(0, entryReadCount);
        }

        [Fact]
        public void GetChatLog_InvalidPointerVectorReturnsEmptyResultWithoutAllocatingIndexBuffer() {
            Exception raisedException = null;
            int readCount = 0;
            ChatLogPointers pointers = CreatePointers(currentArrayIndex: 0);
            pointers.OffsetArrayEnd = pointers.OffsetArrayStart + (65537L * sizeof(int));
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => pointers,
                readBytes: (_, _) => readCount++,
                raiseException: exception => raisedException = exception);
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog();

            Assert.Empty(result.ChatLogItems);
            Assert.IsType<InvalidOperationException>(raisedException);
            Assert.Equal(0, readCount);
        }

        [Fact]
        public void GetChatLog_InvalidLogVectorReturnsEmptyResultAndRaisesException() {
            Exception raisedException = null;
            ChatLogPointers pointers = CreatePointers(currentArrayIndex: 1);
            pointers.LogNext = pointers.LogEnd + 1;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => pointers,
                raiseException: exception => raisedException = exception);
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog();

            Assert.Empty(result.ChatLogItems);
            Assert.IsType<InvalidOperationException>(raisedException);
        }

        [Fact]
        public void GetChatLog_IndexPastLogPositionReturnsEmptyResultAndPreservesCursor() {
            Exception raisedException = null;
            ChatLogPointers pointers = CreatePointers(currentArrayIndex: 2, capacity: 3);
            pointers.LogNext = pointers.LogStart + 20;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => pointers,
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, 0, 10);
                        WriteInt32(destination, 1, 21);
                    }
                },
                raiseException: exception => raisedException = exception);
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog(previousArrayIndex: 1, previousOffset: 10);

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(1, result.PreviousArrayIndex);
            Assert.Equal(10, result.PreviousOffset);
            Assert.IsType<InvalidOperationException>(raisedException);
        }

        [Fact]
        public void GetChatLog_CountResetWithInvalidCurrentEndPreservesCursor() {
            Exception raisedException = null;
            ChatLogPointers pointers = CreatePointers(currentArrayIndex: 1, capacity: 3);
            pointers.LogNext = pointers.LogStart + 20;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => pointers,
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, 0, 21);
                    }
                },
                raiseException: exception => raisedException = exception);
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog(previousArrayIndex: 2, previousOffset: 10);

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(2, result.PreviousArrayIndex);
            Assert.Equal(10, result.PreviousOffset);
            Assert.IsType<InvalidOperationException>(raisedException);
        }

        [Fact]
        public void GetChatLog_WhenDetachedReturnsEmptyResultWithoutReadingPointers() {
            int pointerReadCount = 0;
            ChatLogReader chatLogReader = CreateReader(
                isAttached: () => false,
                readPointers: _ => {
                    pointerReadCount++;
                    return CreatePointers(currentArrayIndex: 1);
                });
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog(previousArrayIndex: 4, previousOffset: 50);

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(4, result.PreviousArrayIndex);
            Assert.Equal(50, result.PreviousOffset);
            Assert.Equal(0, pointerReadCount);
        }

        [Fact]
        public void GetChatLog_InvalidChatAddressReturnsEmptyResultAndPreservesCursor() {
            Exception raisedException = null;
            ChatLogReader chatLogReader = CreateReader(
                getChatLogAddress: () => new IntPtr(20),
                raiseException: exception => raisedException = exception);
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog(previousArrayIndex: 4, previousOffset: 50);

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(4, result.PreviousArrayIndex);
            Assert.Equal(50, result.PreviousOffset);
            Assert.IsType<InvalidOperationException>(raisedException);
        }

        [Fact]
        public void GetChatLog_CapacityChangeReprimesWithoutReturningHistory() {
            int capacity = 4;
            int currentArrayIndex = 3;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex, capacity),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, currentArrayIndex - 1, currentArrayIndex * 10);
                    }
                });
            Reader reader = new Reader(chatLogReader);

            ChatLogResult first = reader.GetChatLog();
            capacity = 2;
            currentArrayIndex = 1;
            ChatLogResult second = reader.GetChatLog(first.PreviousArrayIndex, first.PreviousOffset);

            Assert.Empty(first.ChatLogItems);
            Assert.Empty(second.ChatLogItems);
            Assert.Equal(1, second.PreviousArrayIndex);
            Assert.Equal(10, second.PreviousOffset);
        }

        [Fact]
        public void GetChatLog_CapacityGrowthReprimesWithoutReturningHistory() {
            byte[] entry = CreateEntry("After growth");
            int capacity = 2;
            int currentArrayIndex = 1;
            int entryReadCount = 0;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex, capacity),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, 0, 10);
                        if (currentArrayIndex > 1) {
                            WriteInt32(destination, 1, 10 + entry.Length);
                        }
                    }
                    else {
                        entryReadCount++;
                    }
                });
            Reader reader = new Reader(chatLogReader);

            ChatLogResult first = reader.GetChatLog();
            capacity = 4;
            currentArrayIndex = 2;
            ChatLogResult second = reader.GetChatLog(first.PreviousArrayIndex, first.PreviousOffset);

            Assert.Empty(first.ChatLogItems);
            Assert.Empty(second.ChatLogItems);
            Assert.Equal(2, second.PreviousArrayIndex);
            Assert.Equal(10 + entry.Length, second.PreviousOffset);
            Assert.Equal(0, entryReadCount);
        }

        [Fact]
        public void GetChatLog_IndexSnapshotChangeRetriesBeforeCommittingCursor() {
            int pointerReadCount = 0;
            int indexReadCount = 0;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => {
                    pointerReadCount++;
                    return CreatePointers(currentArrayIndex: pointerReadCount == 1 ? 1 : 2, capacity: 3);
                },
                readBytes: (address, destination) => {
                    Assert.Equal(new IntPtr(IndexStart), address);
                    indexReadCount++;
                    WriteInt32(destination, 0, 10);
                    if (destination.Length >= 2 * sizeof(int)) {
                        WriteInt32(destination, 1, 20);
                    }
                });
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog();

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(2, result.PreviousArrayIndex);
            Assert.Equal(20, result.PreviousOffset);
            Assert.Equal(5, pointerReadCount);
            Assert.Equal(2, indexReadCount);
        }

        [Fact]
        public void GetChatLog_DataSnapshotChangeRetriesWithoutReturningDuplicateEntries() {
            byte[] firstEntry = CreateEntry("First");
            byte[] secondEntry = CreateEntry("Second");
            int pointerReadCount = 0;
            int entryReadCount = 0;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => {
                    pointerReadCount++;
                    return CreatePointers(currentArrayIndex: pointerReadCount <= 2 ? 1 : 2, capacity: 3);
                },
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, 0, firstEntry.Length);
                        if (destination.Length >= 2 * sizeof(int)) {
                            WriteInt32(destination, 1, firstEntry.Length + secondEntry.Length);
                        }

                        return;
                    }

                    entryReadCount++;
                    byte[] source = address == new IntPtr(LogStart) ? firstEntry : secondEntry;
                    Buffer.BlockCopy(source, 0, destination, 0, source.Length);
                });
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog();

            Assert.Collection(
                result.ChatLogItems,
                item => Assert.Equal("First", item.Message),
                item => Assert.Equal("Second", item.Message));
            Assert.Equal(2, result.PreviousArrayIndex);
            Assert.Equal(firstEntry.Length + secondEntry.Length, result.PreviousOffset);
            Assert.Equal(6, pointerReadCount);
            Assert.Equal(3, entryReadCount);
        }

        [Fact]
        public void GetChatLog_WhenProcessExitsDuringPollReturnsEmptyResultAndPreservesCursor() {
            Exception raisedException = null;
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex: 2, capacity: 3),
                readBytes: (_, _) => throw new InvalidOperationException("Process exited."),
                raiseException: exception => raisedException = exception);
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader);

            ChatLogResult result = reader.GetChatLog(previousArrayIndex: 1, previousOffset: 10);

            Assert.Empty(result.ChatLogItems);
            Assert.Equal(1, result.PreviousArrayIndex);
            Assert.Equal(10, result.PreviousOffset);
            Assert.Equal("Process exited.", raisedException?.Message);
        }

        [Theory]
        [InlineData("Configured Name", "Configured Name")]
        [InlineData(null, "UNRESOLVED")]
        [InlineData("  ", "UNRESOLVED")]
        public void GetChatLog_UsesConfiguredCharacterNameOrUnresolved(string configuredName, string expectedName) {
            byte[] entry = CreateEntry("Message");
            ChatLogReader chatLogReader = CreateReader(
                readPointers: _ => CreatePointers(currentArrayIndex: 1, capacity: 3),
                readBytes: (address, destination) => {
                    if (address == new IntPtr(IndexStart)) {
                        WriteInt32(destination, 0, entry.Length);
                    }
                    else {
                        Buffer.BlockCopy(entry, 0, destination, 0, entry.Length);
                    }
                });
            chatLogReader.ChatLogFirstRun = false;
            Reader reader = new Reader(chatLogReader, () => configuredName);

            ChatLogItem item = reader.GetChatLog().ChatLogItems.Single();

            Assert.Equal(expectedName, item.PlayerCharacterName);
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
            Func<bool> canRead = null,
            Func<bool> isAttached = null,
            Func<IntPtr> getChatLogAddress = null,
            Func<IntPtr, ChatLogPointers> readPointers = null,
            Action<IntPtr, byte[]> readBytes = null,
            Action<Exception> raiseException = null) {
            return new ChatLogReader(
                canRead ?? (() => true),
                isAttached ?? (() => true),
                getChatLogAddress ?? (() => new IntPtr(0x1000)),
                readPointers ?? (_ => CreatePointers(currentArrayIndex: 0)),
                readBytes ?? ((_, _) => { }),
                (_, exception) => raiseException?.Invoke(exception));
        }

        private static ChatLogPointers CreatePointers(int currentArrayIndex, int capacity = 1000) {
            return new ChatLogPointers {
                OffsetArrayStart = IndexStart,
                OffsetArrayPos = IndexStart + currentArrayIndex * sizeof(int),
                OffsetArrayEnd = IndexStart + capacity * sizeof(int),
                LogStart = LogStart,
                LogNext = LogStart + 2 * 1024 * 1024,
                LogEnd = LogStart + 2 * 1024 * 1024,
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
