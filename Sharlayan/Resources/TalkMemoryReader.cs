namespace Sharlayan.Resources {
    using System;
    using System.Text;

    using Sharlayan.Models.ReadResults;

    internal delegate bool MemoryPeek(IntPtr address, byte[] buffer, int count);

    internal static class TalkMemoryReader {
        internal const int MaximumStringBytes = 16 * 1024;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static TalkResult Read(
            MemoryPeek peek,
            IntPtr nameAddress,
            IntPtr textAddress,
            TalkMemoryLayout layout) {
            if (peek == null || nameAddress == IntPtr.Zero || textAddress == IntPtr.Zero || layout == null) {
                return TalkResult.Unavailable;
            }

            for (int attempt = 0; attempt < 2; attempt++) {
                if (!TryReadHeader(peek, nameAddress, layout, out Header nameBefore)
                    || !TryReadHeader(peek, textAddress, layout, out Header textBefore)
                    || !TryReadBytes(peek, nameBefore, out byte[] nameBytes)
                    || !TryReadBytes(peek, textBefore, out byte[] textBytes)
                    || !TryReadHeader(peek, nameAddress, layout, out Header nameAfter)
                    || !TryReadHeader(peek, textAddress, layout, out Header textAfter)) {
                    return TalkResult.Unavailable;
                }

                if (!nameBefore.Equals(nameAfter) || !textBefore.Equals(textAfter)) {
                    continue;
                }

                try {
                    return new TalkResult(
                        true,
                        StrictUtf8.GetString(nameBytes, 0, nameBytes.Length),
                        StrictUtf8.GetString(textBytes, 0, textBytes.Length));
                }
                catch (DecoderFallbackException) {
                    return TalkResult.Unavailable;
                }
            }

            return TalkResult.Unavailable;
        }

        private static bool TryReadHeader(MemoryPeek peek, IntPtr address, TalkMemoryLayout layout, out Header header) {
            byte[] bytes = new byte[layout.HeaderSize];
            if (!peek(address, bytes, bytes.Length)) {
                header = default(Header);
                return false;
            }

            long pointer = BitConverter.ToInt64(bytes, layout.StringPointerOffset);
            long bufferUsed = BitConverter.ToInt64(bytes, layout.BufferUsedOffset);
            long stringLength = BitConverter.ToInt64(bytes, layout.StringLengthOffset);
            if (pointer == 0
                || stringLength < 0
                || stringLength > MaximumStringBytes
                || bufferUsed < stringLength
                || bufferUsed > MaximumStringBytes + 1L) {
                header = default(Header);
                return false;
            }

            header = new Header(new IntPtr(pointer), (int)bufferUsed, (int)stringLength);
            return true;
        }

        private static bool TryReadBytes(MemoryPeek peek, Header header, out byte[] value) {
            int readLength = header.BufferUsed > header.StringLength ? header.StringLength + 1 : header.StringLength;
            byte[] bytes = new byte[readLength];
            if (readLength > 0 && !peek(header.Pointer, bytes, readLength)) {
                value = null;
                return false;
            }

            if (readLength > header.StringLength && bytes[header.StringLength] != 0) {
                value = null;
                return false;
            }

            value = new byte[header.StringLength];
            if (value.Length > 0) Buffer.BlockCopy(bytes, 0, value, 0, value.Length);
            return true;
        }

        private struct Header : IEquatable<Header> {
            internal Header(IntPtr pointer, int bufferUsed, int stringLength) {
                this.Pointer = pointer;
                this.BufferUsed = bufferUsed;
                this.StringLength = stringLength;
            }

            internal IntPtr Pointer { get; }
            internal int BufferUsed { get; }
            internal int StringLength { get; }

            public bool Equals(Header other) {
                return this.Pointer == other.Pointer && this.BufferUsed == other.BufferUsed && this.StringLength == other.StringLength;
            }
        }
    }
}
