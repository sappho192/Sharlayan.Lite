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
        private const int BufferSize = 4000;

        private readonly Func<bool> _canRead;

        private readonly Func<IntPtr> _getChatLogAddress;

        private readonly byte[] _indexBuffer = new byte[BufferSize];

        private readonly Func<bool> _isAttached;

        private readonly Action<Logger, Exception> _raiseException;

        private readonly Action<IntPtr, byte[]> _readBytes;

        private readonly Func<IntPtr, ChatLogPointers> _readPointers;

        public readonly List<int> Indexes = new List<int>(BufferSize / sizeof(int));

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
                memoryHandler.GetByteArray,
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

        public void EnsureArrayIndexes() {
            this.Indexes.Clear();

            Array.Clear(this._indexBuffer, 0, this._indexBuffer.Length);
            this._readBytes(new IntPtr(this.ChatLogPointers.OffsetArrayStart), this._indexBuffer);
            for (int i = 0; i < BufferSize; i += sizeof(int)) {
                this.Indexes.Add(BitConverter.ToInt32(this._indexBuffer, i));
            }
        }

        public IEnumerable<byte[]> ResolveEntries(int offset, int length) {
            List<byte[]> entries = new List<byte[]>();

            for (int i = offset; i < length; i++) {
                int currentOffset = this.Indexes[i];

                byte[] entry = this.ResolveEntry(this.PreviousOffset, currentOffset);
                if (entry.Length > 0) {
                    entries.Add(entry);
                }

                this.PreviousOffset = currentOffset;
            }

            return entries;
        }

        private byte[] ResolveEntry(int offset, int length) {
            int size = length - offset;

            byte[] result = new byte[size];

            if (size == 0) {
                return result;
            }

            this._readBytes(new IntPtr(this.ChatLogPointers.LogStart + offset), result);

            return result;
        }
    }
}
