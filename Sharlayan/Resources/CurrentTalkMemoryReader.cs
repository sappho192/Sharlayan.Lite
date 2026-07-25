namespace Sharlayan.Resources {
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Sharlayan.Models.ReadResults;

    internal static class CurrentTalkMemoryReader {
        private const int MaximumAtkValueCount = 4096;
        private const int StringChunkSize = 256;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static TalkResult Read(
            MemoryPeek peek,
            IntPtr uiModulePointerAddress,
            CurrentTalkMemoryLayout layout) {
            if (peek == null || uiModulePointerAddress == IntPtr.Zero || layout == null) {
                return TalkResult.Unavailable;
            }

            for (int attempt = 0; attempt < 2; attempt++) {
                if (!TryCapture(peek, uiModulePointerAddress, layout, out Snapshot before)
                    || !TryReadNullTerminated(peek, before.NameStringPointer, out byte[] nameBytes)
                    || !TryReadNullTerminated(peek, before.TextStringPointer, out byte[] textBytes)
                    || !TryCaptureKnownAddon(peek, uiModulePointerAddress, layout, before, out Snapshot after)
                    || !before.Equals(after)
                    || !TryReadNullTerminated(peek, after.NameStringPointer, out byte[] stableNameBytes)
                    || !TryReadNullTerminated(peek, after.TextStringPointer, out byte[] stableTextBytes)
                    || !BytesEqual(nameBytes, stableNameBytes)
                    || !BytesEqual(textBytes, stableTextBytes)
                    || !TryCaptureKnownAddon(peek, uiModulePointerAddress, layout, after, out Snapshot final)
                    || !after.Equals(final)) {
                    continue;
                }

                try {
                    return new TalkResult(
                        true,
                        StrictUtf8.GetString(nameBytes, 0, nameBytes.Length),
                        StrictUtf8.GetString(textBytes, 0, textBytes.Length),
                        TalkSource.Current,
                        true);
                }
                catch (DecoderFallbackException) {
                    return TalkResult.Unavailable;
                }
            }

            return TalkResult.Unavailable;
        }

        private static bool TryCapture(
            MemoryPeek peek,
            IntPtr uiModulePointerAddress,
            CurrentTalkMemoryLayout layout,
            out Snapshot snapshot) {
            snapshot = default(Snapshot);
            if (!TryReadPointer(peek, uiModulePointerAddress, out IntPtr uiModule)
                || !TryAdd(uiModule, layout.RaptureAtkModuleOffset, out IntPtr raptureAtkModule)
                || !TryAdd(raptureAtkModule, layout.RaptureAtkUnitManagerOffset, out IntPtr unitManager)
                || !TryAdd(unitManager, layout.AllLoadedUnitsListOffset, out IntPtr unitList)
                || !TryAdd(unitList, layout.CountOffset, out IntPtr countAddress)
                || !TryReadUInt16(peek, countAddress, out ushort count)
                || count > layout.Capacity
                || !TryAdd(unitList, layout.EntriesOffset, out IntPtr entries)) {
                return false;
            }

            for (int index = 0; index < count; index++) {
                if (!TryAdd(entries, (long)index * layout.EntrySize, out IntPtr entryAddress)
                    || !TryReadPointer(peek, entryAddress, out IntPtr addon)) {
                    return false;
                }

                if (addon == IntPtr.Zero || !IsExpectedAddon(peek, addon, layout)) {
                    continue;
                }

                return TryCaptureAddon(
                    peek,
                    uiModulePointerAddress,
                    uiModule,
                    countAddress,
                    count,
                    entryAddress,
                    addon,
                    layout,
                    out snapshot);
            }

            return false;
        }

        private static bool TryCaptureKnownAddon(
            MemoryPeek peek,
            IntPtr uiModulePointerAddress,
            CurrentTalkMemoryLayout layout,
            Snapshot expected,
            out Snapshot snapshot) {
            snapshot = default(Snapshot);
            if (!TryReadPointer(peek, uiModulePointerAddress, out IntPtr uiModule)
                || uiModule != expected.UiModule
                || !TryReadUInt16(peek, expected.CountAddress, out ushort count)
                || count != expected.Count
                || !TryReadPointer(peek, expected.EntryAddress, out IntPtr addon)
                || addon != expected.Addon
                || !IsExpectedAddon(peek, addon, layout)) {
                return false;
            }

            return TryCaptureAddon(
                peek,
                uiModulePointerAddress,
                uiModule,
                expected.CountAddress,
                count,
                expected.EntryAddress,
                addon,
                layout,
                out snapshot);
        }

        private static bool TryCaptureAddon(
            MemoryPeek peek,
            IntPtr uiModulePointerAddress,
            IntPtr uiModule,
            IntPtr countAddress,
            ushort count,
            IntPtr entryAddress,
            IntPtr addon,
            CurrentTalkMemoryLayout layout,
            out Snapshot snapshot) {
            snapshot = default(Snapshot);
            if (!TryAdd(addon, layout.VisibilityStateOffset, out IntPtr visibilityAddress)
                || !TryReadUInt32(peek, visibilityAddress, out uint visibilityState)
                || (visibilityState & layout.VisibilityMask) == 0
                || !TryAdd(addon, layout.ReadinessOffset, out IntPtr readinessAddress)
                || !TryReadByte(peek, readinessAddress, out byte readiness)
                || (readiness & layout.ReadinessMask) == 0
                || !TryAdd(addon, layout.AtkValuesPointerOffset, out IntPtr atkValuesPointerAddress)
                || !TryReadPointer(peek, atkValuesPointerAddress, out IntPtr atkValues)
                || !TryAdd(addon, layout.AtkValuesCountOffset, out IntPtr atkValuesCountAddress)
                || !TryReadUInt16(peek, atkValuesCountAddress, out ushort atkValuesCount)
                || atkValuesCount > MaximumAtkValueCount
                || atkValuesCount <= Math.Max(layout.TextValueIndex, layout.NameValueIndex)
                || !TryReadAtkStringPointer(peek, atkValues, layout.TextValueIndex, layout, out int textType, out IntPtr textStringPointer)
                || !TryReadAtkStringPointer(peek, atkValues, layout.NameValueIndex, layout, out int nameType, out IntPtr nameStringPointer)) {
                return false;
            }

            snapshot = new Snapshot(
                uiModulePointerAddress,
                uiModule,
                countAddress,
                count,
                entryAddress,
                addon,
                visibilityState,
                readiness,
                atkValues,
                atkValuesCount,
                textType,
                textStringPointer,
                nameType,
                nameStringPointer);
            return true;
        }

        private static bool TryReadAtkStringPointer(
            MemoryPeek peek,
            IntPtr atkValues,
            int index,
            CurrentTalkMemoryLayout layout,
            out int type,
            out IntPtr stringPointer) {
            type = 0;
            stringPointer = IntPtr.Zero;
            if (!TryAdd(atkValues, (long)index * layout.AtkValueSize, out IntPtr atkValue)
                || !TryAdd(atkValue, layout.AtkValueTypeOffset, out IntPtr typeAddress)
                || !TryReadInt32(peek, typeAddress, out type)
                || Array.IndexOf(layout.AllowedStringTypes, type) < 0
                || !TryAdd(atkValue, layout.AtkValueValueOffset, out IntPtr valueAddress)
                || !TryReadPointer(peek, valueAddress, out stringPointer)
                || stringPointer == IntPtr.Zero) {
                return false;
            }

            return true;
        }

        private static bool IsExpectedAddon(MemoryPeek peek, IntPtr addon, CurrentTalkMemoryLayout layout) {
            if (!TryAdd(addon, layout.AddonNameOffset, out IntPtr nameAddress)) {
                return false;
            }

            byte[] bytes = new byte[layout.AddonNameCapacity];
            if (!peek(nameAddress, bytes, bytes.Length)) {
                return false;
            }

            byte[] expected = Encoding.ASCII.GetBytes(layout.AddonName);
            if (expected.Length >= bytes.Length || bytes[expected.Length] != 0) {
                return false;
            }

            for (int index = 0; index < expected.Length; index++) {
                if (bytes[index] != expected[index]) {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadNullTerminated(MemoryPeek peek, IntPtr address, out byte[] value) {
            List<byte> bytes = new List<byte>();
            for (int offset = 0; offset < TalkMemoryReader.MaximumStringBytes; offset += StringChunkSize) {
                int count = Math.Min(StringChunkSize, TalkMemoryReader.MaximumStringBytes - offset);
                if (!TryAdd(address, offset, out IntPtr chunkAddress)) {
                    value = null;
                    return false;
                }

                byte[] chunk = new byte[count];
                if (peek(chunkAddress, chunk, count)) {
                    int nullIndex = Array.IndexOf(chunk, (byte)0);
                    if (nullIndex >= 0) {
                        for (int index = 0; index < nullIndex; index++) bytes.Add(chunk[index]);
                        value = bytes.ToArray();
                        return true;
                    }

                    bytes.AddRange(chunk);
                    continue;
                }

                for (int index = 0; index < count; index++) {
                    if (!TryAdd(chunkAddress, index, out IntPtr byteAddress)) {
                        value = null;
                        return false;
                    }

                    byte[] single = new byte[1];
                    if (!peek(byteAddress, single, 1)) {
                        value = null;
                        return false;
                    }

                    if (single[0] == 0) {
                        value = bytes.ToArray();
                        return true;
                    }

                    bytes.Add(single[0]);
                }
            }

            value = null;
            return false;
        }

        private static bool TryReadPointer(MemoryPeek peek, IntPtr address, out IntPtr value) {
            byte[] bytes = new byte[8];
            if (!peek(address, bytes, bytes.Length)) {
                value = IntPtr.Zero;
                return false;
            }

            long raw = BitConverter.ToInt64(bytes, 0);
            value = raw == 0 ? IntPtr.Zero : new IntPtr(raw);
            return true;
        }

        private static bool TryReadInt32(MemoryPeek peek, IntPtr address, out int value) {
            byte[] bytes = new byte[4];
            if (!peek(address, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = BitConverter.ToInt32(bytes, 0);
            return true;
        }

        private static bool TryReadUInt32(MemoryPeek peek, IntPtr address, out uint value) {
            byte[] bytes = new byte[4];
            if (!peek(address, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = BitConverter.ToUInt32(bytes, 0);
            return true;
        }

        private static bool TryReadUInt16(MemoryPeek peek, IntPtr address, out ushort value) {
            byte[] bytes = new byte[2];
            if (!peek(address, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = BitConverter.ToUInt16(bytes, 0);
            return true;
        }

        private static bool TryReadByte(MemoryPeek peek, IntPtr address, out byte value) {
            byte[] bytes = new byte[1];
            if (!peek(address, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = bytes[0];
            return true;
        }

        private static bool TryAdd(IntPtr address, long offset, out IntPtr result) {
            try {
                result = new IntPtr(checked(address.ToInt64() + offset));
                return true;
            }
            catch (OverflowException) {
                result = IntPtr.Zero;
                return false;
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right) {
            if (left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++) {
                if (left[index] != right[index]) return false;
            }

            return true;
        }

        private struct Snapshot : IEquatable<Snapshot> {
            internal Snapshot(
                IntPtr uiModulePointerAddress,
                IntPtr uiModule,
                IntPtr countAddress,
                ushort count,
                IntPtr entryAddress,
                IntPtr addon,
                uint visibilityState,
                byte readiness,
                IntPtr atkValues,
                ushort atkValuesCount,
                int textType,
                IntPtr textStringPointer,
                int nameType,
                IntPtr nameStringPointer) {
                this.UiModulePointerAddress = uiModulePointerAddress;
                this.UiModule = uiModule;
                this.CountAddress = countAddress;
                this.Count = count;
                this.EntryAddress = entryAddress;
                this.Addon = addon;
                this.VisibilityState = visibilityState;
                this.Readiness = readiness;
                this.AtkValues = atkValues;
                this.AtkValuesCount = atkValuesCount;
                this.TextType = textType;
                this.TextStringPointer = textStringPointer;
                this.NameType = nameType;
                this.NameStringPointer = nameStringPointer;
            }

            internal IntPtr UiModulePointerAddress { get; }
            internal IntPtr UiModule { get; }
            internal IntPtr CountAddress { get; }
            internal ushort Count { get; }
            internal IntPtr EntryAddress { get; }
            internal IntPtr Addon { get; }
            internal uint VisibilityState { get; }
            internal byte Readiness { get; }
            internal IntPtr AtkValues { get; }
            internal ushort AtkValuesCount { get; }
            internal int TextType { get; }
            internal IntPtr TextStringPointer { get; }
            internal int NameType { get; }
            internal IntPtr NameStringPointer { get; }

            public bool Equals(Snapshot other) {
                return this.UiModulePointerAddress == other.UiModulePointerAddress
                       && this.UiModule == other.UiModule
                       && this.CountAddress == other.CountAddress
                       && this.Count == other.Count
                       && this.EntryAddress == other.EntryAddress
                       && this.Addon == other.Addon
                       && this.VisibilityState == other.VisibilityState
                       && this.Readiness == other.Readiness
                       && this.AtkValues == other.AtkValues
                       && this.AtkValuesCount == other.AtkValuesCount
                       && this.TextType == other.TextType
                       && this.TextStringPointer == other.TextStringPointer
                       && this.NameType == other.NameType
                       && this.NameStringPointer == other.NameStringPointer;
            }
        }
    }
}
