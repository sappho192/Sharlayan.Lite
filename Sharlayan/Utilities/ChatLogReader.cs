// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ChatLogReader.cs" company="SyndicatedLife">
//   Copyright© 2007 - 2022 Ryan Wilson <syndicated.life@gmail.com> (https://syndicated.life/)
//   Licensed under the MIT license. See LICENSE.md in the solution root for full license information.
// </copyright>
// <summary>
//   ChatLogReader.cs Implementation
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sharlayan.Utilities {
    using System;
    using System.Collections.Generic;

    using NLog;

    using Sharlayan.Models;

    internal class ChatLogReader {
        private const int MaxArrayCapacity = 65536;

        private const int MaxEntrySize = 1024 * 1024;

        private readonly Func<bool> _canRead;

        private readonly Func<IntPtr> _getChatLogAddress;

        private byte[] _indexBuffer = Array.Empty<byte>();

        private readonly Func<bool> _isAttached;

        private readonly Action<Logger, Exception> _raiseException;

        private readonly Action<IntPtr, byte[]> _readBytes;

        private readonly Func<IntPtr, ChatLogPointers> _readPointers;

        private int _previousArrayCapacity;

        private long _previousLogStart;

        private long _previousOffsetArrayStart;

        private bool _hasPointerSnapshot;

        public readonly List<int> Indexes = new List<int>();

        public bool ChatLogFirstRun = true;

        public ChatLogPointers ChatLogPointers;

        public int PreviousArrayIndex;

        public int PreviousOffset;

        public ChatLogReader(MemoryHandler memoryHandler)
            : this(
                () => memoryHandler.Scanner.Locations.ContainsKey(Signatures.CHATLOG_KEY),
                () => memoryHandler.IsAttached,
                () => memoryHandler.Scanner.Locations[Signatures.CHATLOG_KEY],
                address => new ChatLogPointers {
                    LineCount = memoryHandler.GetUInt32(address),
                    OffsetArrayStart = memoryHandler.GetInt64(address, memoryHandler.Structures.ChatLogPointers.OffsetArrayStart),
                    OffsetArrayPos = memoryHandler.GetInt64(address, memoryHandler.Structures.ChatLogPointers.OffsetArrayPos),
                    OffsetArrayEnd = memoryHandler.GetInt64(address, memoryHandler.Structures.ChatLogPointers.OffsetArrayEnd),
                    LogStart = memoryHandler.GetInt64(address, memoryHandler.Structures.ChatLogPointers.LogStart),
                    LogNext = memoryHandler.GetInt64(address, memoryHandler.Structures.ChatLogPointers.LogNext),
                    LogEnd = memoryHandler.GetInt64(address, memoryHandler.Structures.ChatLogPointers.LogEnd),
                },
                (address, destination) => {
                    if (!memoryHandler.Peek(address, destination)) {
                        throw new InvalidOperationException($"Unable to read chat memory at 0x{address.ToInt64():X}.");
                    }
                },
                memoryHandler.RaiseException) {
        }

        internal ChatLogReader(
            Func<bool> canRead,
            Func<bool> isAttached,
            Func<IntPtr> getChatLogAddress,
            Func<IntPtr, ChatLogPointers> readPointers,
            Action<IntPtr, byte[]> readBytes,
            Action<Logger, Exception> raiseException) {
            this._canRead = canRead;
            this._isAttached = isAttached;
            this._getChatLogAddress = getChatLogAddress;
            this._readPointers = readPointers;
            this._readBytes = readBytes;
            this._raiseException = raiseException;
        }

        internal bool CanRead => this._canRead();

        internal bool IsAttached => this._isAttached();

        internal IntPtr GetChatLogAddress() {
            return this._getChatLogAddress();
        }

        internal ChatLogPointers ReadPointers(IntPtr address) {
            return this._readPointers(address);
        }

        internal void RaiseException(Logger logger, Exception exception) {
            this._raiseException(logger, exception);
        }

        public int GetArrayCapacity(out int currentArrayIndex) {
            long start = this.ChatLogPointers.OffsetArrayStart;
            long position = this.ChatLogPointers.OffsetArrayPos;
            long end = this.ChatLogPointers.OffsetArrayEnd;

            if (start <= 0 || start > position || position > end || start % sizeof(int) != 0 || position % sizeof(int) != 0 || end % sizeof(int) != 0) {
                throw new InvalidOperationException($"Invalid chat index vector: start=0x{start:X}, position=0x{position:X}, end=0x{end:X}.");
            }

            long byteLength = end - start;
            long positionOffset = position - start;
            long capacity = byteLength / sizeof(int);
            if (byteLength == 0 || byteLength % sizeof(int) != 0 || capacity > MaxArrayCapacity) {
                throw new InvalidOperationException($"Invalid chat index capacity: {capacity}.");
            }

            currentArrayIndex = (int) (positionOffset / sizeof(int));
            this.ValidateLogPointers();
            return (int) capacity;
        }

        public void EnsureArrayIndexes(int capacity) {
            if (capacity <= 0 || capacity > MaxArrayCapacity) {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            int byteLength = checked(capacity * sizeof(int));
            if (this._indexBuffer.Length != byteLength) {
                this._indexBuffer = new byte[byteLength];
            }

            this.Indexes.Clear();

            Array.Clear(this._indexBuffer, 0, this._indexBuffer.Length);
            this._readBytes(new IntPtr(this.ChatLogPointers.OffsetArrayStart), this._indexBuffer);
            for (int i = 0; i < byteLength; i += sizeof(int)) {
                this.Indexes.Add(BitConverter.ToInt32(this._indexBuffer, i));
            }
        }

        public IEnumerable<byte[]> ResolveEntries(int offset, int length) {
            List<byte[]> entries = this.ResolveEntries(offset, length, this.PreviousOffset, this.GetLogPosition(), out int resolvedOffset);
            this.PreviousOffset = resolvedOffset;
            return entries;
        }

        internal List<byte[]> ResolveEntries(int offset, int length, int previousOffset, int maximumOffset, out int resolvedOffset) {
            if (offset < 0 || length < offset || length > this.Indexes.Count) {
                throw new InvalidOperationException($"Invalid chat index range: offset={offset}, length={length}, capacity={this.Indexes.Count}.");
            }

            List<byte[]> entries = new List<byte[]>();
            resolvedOffset = previousOffset;

            for (int i = offset; i < length; i++) {
                int currentOffset = this.Indexes[i];

                byte[] entry = this.ResolveEntry(resolvedOffset, currentOffset, maximumOffset);
                if (entry.Length > 0) {
                    entries.Add(entry);
                }

                resolvedOffset = currentOffset;
            }

            return entries;
        }

        internal int GetLogCapacity() {
            return checked((int) this.ValidateLogPointers());
        }

        internal int GetLogPosition() {
            this.ValidateLogPointers();
            return checked((int) (this.ChatLogPointers.LogNext - this.ChatLogPointers.LogStart));
        }

        internal bool HasPointerVectorChanged(int arrayCapacity) {
            return this._hasPointerSnapshot && (this._previousArrayCapacity != arrayCapacity || this._previousOffsetArrayStart != this.ChatLogPointers.OffsetArrayStart || this._previousLogStart != this.ChatLogPointers.LogStart);
        }

        internal bool IsCursorBoundary(int arrayIndex, int offset) {
            return arrayIndex == 0 ? offset == 0 : arrayIndex <= this.Indexes.Count && this.Indexes[arrayIndex - 1] == offset;
        }

        internal void RememberPointerVector(int arrayCapacity) {
            this._previousArrayCapacity = arrayCapacity;
            this._previousOffsetArrayStart = this.ChatLogPointers.OffsetArrayStart;
            this._previousLogStart = this.ChatLogPointers.LogStart;
            this._hasPointerSnapshot = true;
        }

        private byte[] ResolveEntry(int offset, int length, int maximumOffset) {
            int logCapacity = this.GetLogCapacity();
            if (maximumOffset < 0 || maximumOffset > logCapacity) {
                throw new InvalidOperationException($"Invalid chat log read boundary: {maximumOffset}.");
            }

            if (offset < 0 || length < 0 || offset > maximumOffset || length > maximumOffset) {
                throw new InvalidOperationException($"Invalid chat log offsets: previous={offset}, current={length}, maximum={maximumOffset}.");
            }

            int size = length - offset;
            if (size < 0 || size > MaxEntrySize) {
                throw new InvalidOperationException($"Invalid chat entry size: {size}.");
            }

            if (size == 0) {
                return Array.Empty<byte>();
            }

            byte[] result = new byte[size];

            this._readBytes(new IntPtr(this.ChatLogPointers.LogStart + offset), result);

            return result;
        }

        private long ValidateLogPointers() {
            long start = this.ChatLogPointers.LogStart;
            long next = this.ChatLogPointers.LogNext;
            long end = this.ChatLogPointers.LogEnd;
            if (start <= 0 || start > next || next > end) {
                throw new InvalidOperationException($"Invalid chat log vector: start=0x{start:X}, next=0x{next:X}, end=0x{end:X}.");
            }

            long length = end - start;
            if (length > int.MaxValue) {
                throw new InvalidOperationException($"Invalid chat log capacity: {length}.");
            }

            return length;
        }
    }
}
