namespace Sharlayan.Resources {
    using System;
    using System.Collections.Generic;
    using System.Text;

    internal sealed class BattleTalkMemoryObservation {
        internal BattleTalkMemoryObservation(bool isAvailable, bool isVisible, string name, string text) {
            this.IsAvailable = isAvailable;
            this.IsVisible = isVisible;
            this.Name = name ?? string.Empty;
            this.Text = text ?? string.Empty;
        }

        internal bool IsAvailable { get; }
        internal bool IsVisible { get; }
        internal string Name { get; }
        internal string Text { get; }

        internal static BattleTalkMemoryObservation Hidden { get; } =
            new BattleTalkMemoryObservation(true, false, string.Empty, string.Empty);

        internal static BattleTalkMemoryObservation Unavailable { get; } =
            new BattleTalkMemoryObservation(false, false, string.Empty, string.Empty);
    }

    internal static class BattleTalkMemoryReader {
        private const int MaximumArraySize = 4096;
        private const int MaximumStringBytes = 65536;
        private const int StringChunkSize = 256;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static BattleTalkMemoryObservation Read(
            MemoryPeek peek,
            IntPtr uiModulePointerAddress,
            BattleTalkMemoryLayout layout) {
            if (peek == null || uiModulePointerAddress == IntPtr.Zero || layout == null) {
                return BattleTalkMemoryObservation.Unavailable;
            }

            for (int attempt = 0; attempt < 2; attempt++) {
                Snapshot before;
                Snapshot after;
                Snapshot final;
                if (!TryCapture(peek, uiModulePointerAddress, layout, out before)) continue;
                if (!before.IsFound) {
                    if (TryCapture(peek, uiModulePointerAddress, layout, out after)
                        && before.Equals(after)) {
                        return BattleTalkMemoryObservation.Hidden;
                    }

                    continue;
                }

                if (!before.IsReady) continue;
                if (!before.IsVisible) {
                    if (TryCapture(peek, uiModulePointerAddress, layout, out after)
                        && before.Equals(after)) {
                        return BattleTalkMemoryObservation.Hidden;
                    }

                    continue;
                }

                byte[] name;
                byte[] text;
                byte[] stableName;
                byte[] stableText;
                if (!TryReadNullTerminated(peek, before.NamePointer, out name)
                    || !TryReadNullTerminated(peek, before.TextPointer, out text)
                    || !TryCapture(peek, uiModulePointerAddress, layout, out after)
                    || !before.Equals(after)
                    || !TryReadNullTerminated(peek, after.NamePointer, out stableName)
                    || !TryReadNullTerminated(peek, after.TextPointer, out stableText)
                    || !BytesEqual(name, stableName)
                    || !BytesEqual(text, stableText)
                    || !TryCapture(peek, uiModulePointerAddress, layout, out final)
                    || !after.Equals(final)) {
                    continue;
                }

                try {
                    return new BattleTalkMemoryObservation(
                        true,
                        true,
                        StrictUtf8.GetString(name),
                        StrictUtf8.GetString(text));
                }
                catch (DecoderFallbackException) {
                    return BattleTalkMemoryObservation.Unavailable;
                }
            }

            return BattleTalkMemoryObservation.Unavailable;
        }

        private static bool TryCapture(
            MemoryPeek peek,
            IntPtr uiModulePointerAddress,
            BattleTalkMemoryLayout layout,
            out Snapshot snapshot) {
            snapshot = null;
            IntPtr uiModule;
            IntPtr raptureAtkModule;
            IntPtr unitManager;
            IntPtr unitList;
            IntPtr countAddress;
            IntPtr entries;
            ushort count;
            if (!TryReadPointer(peek, uiModulePointerAddress, out uiModule)
                || uiModule == IntPtr.Zero
                || !TryAdd(uiModule, layout.RaptureAtkModuleOffset, out raptureAtkModule)
                || !TryAdd(raptureAtkModule, layout.RaptureAtkUnitManagerOffset, out unitManager)
                || !TryAdd(unitManager, layout.AllLoadedUnitsListOffset, out unitList)
                || !TryAdd(unitList, layout.CountOffset, out countAddress)
                || !TryReadUInt16(peek, countAddress, out count)
                || count > layout.Capacity
                || !TryAdd(unitList, layout.EntriesOffset, out entries)) {
                return false;
            }

            IntPtr[] addons = new IntPtr[count];
            int foundIndex = -1;
            for (int index = 0; index < count; index++) {
                IntPtr entry;
                if (!TryAdd(entries, (long) index * layout.EntrySize, out entry)
                    || !TryReadPointer(peek, entry, out addons[index])) {
                    return false;
                }

                if (addons[index] != IntPtr.Zero && IsExpectedAddon(peek, addons[index], layout)) {
                    if (foundIndex >= 0) return false;
                    foundIndex = index;
                }
            }

            if (foundIndex < 0) {
                snapshot = new Snapshot(uiModule, unitList, count, addons);
                return true;
            }

            IntPtr addon = addons[foundIndex];
            IntPtr visibilityAddress;
            IntPtr readinessAddress;
            uint visibility;
            byte readiness;
            if (!TryAdd(addon, layout.VisibilityStateOffset, out visibilityAddress)
                || !TryReadUInt32(peek, visibilityAddress, out visibility)
                || !TryAdd(addon, layout.ReadinessOffset, out readinessAddress)
                || !TryReadByte(peek, readinessAddress, out readiness)) {
                return false;
            }

            bool isVisible = (visibility & layout.VisibilityMask) != 0;
            bool isReady = (readiness & layout.ReadinessMask) != 0;
            if (!isVisible || !isReady) {
                snapshot = new Snapshot(
                    uiModule,
                    unitList,
                    count,
                    addons,
                    foundIndex,
                    visibility,
                    readiness,
                    isVisible,
                    isReady);
                return true;
            }

            IntPtr holder;
            short numberCount;
            short stringCount;
            IntPtr numberArrays;
            IntPtr stringArrays;
            IntPtr numberData;
            IntPtr stringData;
            int numberSize;
            int stringSize;
            byte numberUpdate;
            byte stringUpdate;
            IntPtr numberValues;
            IntPtr stringValues;
            int visibleValue;
            IntPtr namePointer;
            IntPtr textPointer;
            if (!TryAdd(raptureAtkModule, layout.AtkArrayDataHolderOffset, out holder)
                || !TryReadInt16At(peek, holder, layout.NumberArrayCountOffset, out numberCount)
                || numberCount <= layout.NumberArrayId
                || !TryReadPointerAt(peek, holder, layout.NumberArraysOffset, out numberArrays)
                || !TryReadPointerAt(peek, numberArrays, (long) layout.NumberArrayId * 8, out numberData)
                || numberData == IntPtr.Zero
                || !TryReadInt32At(peek, numberData, layout.ArraySizeOffset, out numberSize)
                || numberSize <= layout.VisibleIndex
                || numberSize > MaximumArraySize
                || !TryReadByteAt(peek, numberData, layout.ArrayUpdateStateOffset, out numberUpdate)
                || !TryReadPointerAt(peek, numberData, layout.NumberValuesOffset, out numberValues)
                || !TryReadInt32At(peek, numberValues, (long) layout.VisibleIndex * 4, out visibleValue)
                || visibleValue == 0
                || !TryReadInt16At(peek, holder, layout.StringArrayCountOffset, out stringCount)
                || stringCount <= layout.StringArrayId
                || !TryReadPointerAt(peek, holder, layout.StringArraysOffset, out stringArrays)
                || !TryReadPointerAt(peek, stringArrays, (long) layout.StringArrayId * 8, out stringData)
                || stringData == IntPtr.Zero
                || !TryReadInt32At(peek, stringData, layout.ArraySizeOffset, out stringSize)
                || stringSize <= Math.Max(layout.NameIndex, layout.TextIndex)
                || stringSize > MaximumArraySize
                || !TryReadByteAt(peek, stringData, layout.ArrayUpdateStateOffset, out stringUpdate)
                || !TryReadPointerAt(peek, stringData, layout.StringValuesOffset, out stringValues)
                || !TryReadPointerAt(peek, stringValues, (long) layout.NameIndex * 8, out namePointer)
                || namePointer == IntPtr.Zero
                || !TryReadPointerAt(peek, stringValues, (long) layout.TextIndex * 8, out textPointer)
                || textPointer == IntPtr.Zero) {
                return false;
            }

            snapshot = new Snapshot(
                uiModule,
                unitList,
                count,
                addons,
                foundIndex,
                visibility,
                readiness,
                true,
                true,
                numberData,
                numberSize,
                numberUpdate,
                numberValues,
                visibleValue,
                stringData,
                stringSize,
                stringUpdate,
                stringValues,
                namePointer,
                textPointer);
            return true;
        }

        private static bool TryReadNullTerminated(
            MemoryPeek peek,
            IntPtr pointer,
            out byte[] value) {
            List<byte> bytes = new List<byte>();
            byte[] chunk = new byte[StringChunkSize];
            for (int offset = 0; offset < MaximumStringBytes; offset += chunk.Length) {
                IntPtr address;
                int count = Math.Min(chunk.Length, MaximumStringBytes - offset);
                if (!TryAdd(pointer, offset, out address)) {
                    value = null;
                    return false;
                }

                if (peek(address, chunk, count)) {
                    for (int index = 0; index < count; index++) {
                        if (chunk[index] == 0) {
                            return TryFinishUtf8(bytes, out value);
                        }

                        bytes.Add(chunk[index]);
                    }

                    continue;
                }

                for (int index = 0; index < count; index++) {
                    IntPtr byteAddress;
                    byte single;
                    if (!TryAdd(address, index, out byteAddress)
                        || !TryReadByte(peek, byteAddress, out single)) {
                        value = null;
                        return false;
                    }

                    if (single == 0) return TryFinishUtf8(bytes, out value);
                    bytes.Add(single);
                }
            }

            value = null;
            return false;
        }

        private static bool TryFinishUtf8(List<byte> bytes, out byte[] value) {
            value = bytes.ToArray();
            try {
                StrictUtf8.GetString(value);
                return true;
            }
            catch (DecoderFallbackException) {
                value = null;
                return false;
            }
        }

        private static bool IsExpectedAddon(
            MemoryPeek peek,
            IntPtr addon,
            BattleTalkMemoryLayout layout) {
            IntPtr nameAddress;
            if (!TryAdd(addon, layout.AddonNameOffset, out nameAddress)) return false;
            byte[] bytes = new byte[layout.AddonNameCapacity];
            if (!peek(nameAddress, bytes, bytes.Length)) return false;
            byte[] expected = Encoding.ASCII.GetBytes(layout.AddonName);
            if (expected.Length >= bytes.Length || bytes[expected.Length] != 0) return false;
            for (int index = 0; index < expected.Length; index++) {
                if (bytes[index] != expected[index]) return false;
            }

            return true;
        }

        private static bool TryReadPointerAt(
            MemoryPeek peek,
            IntPtr address,
            long offset,
            out IntPtr value) {
            IntPtr target;
            value = IntPtr.Zero;
            return TryAdd(address, offset, out target) && TryReadPointer(peek, target, out value);
        }

        private static bool TryReadPointer(MemoryPeek peek, IntPtr address, out IntPtr value) {
            byte[] bytes = new byte[8];
            if (!peek(address, bytes, bytes.Length)) {
                value = IntPtr.Zero;
                return false;
            }

            value = new IntPtr(BitConverter.ToInt64(bytes, 0));
            return true;
        }

        private static bool TryReadInt16At(
            MemoryPeek peek,
            IntPtr address,
            long offset,
            out short value) {
            IntPtr target;
            byte[] bytes = new byte[2];
            if (!TryAdd(address, offset, out target) || !peek(target, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = BitConverter.ToInt16(bytes, 0);
            return true;
        }

        private static bool TryReadInt32At(
            MemoryPeek peek,
            IntPtr address,
            long offset,
            out int value) {
            IntPtr target;
            byte[] bytes = new byte[4];
            if (!TryAdd(address, offset, out target) || !peek(target, bytes, bytes.Length)) {
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

        private static bool TryReadByteAt(
            MemoryPeek peek,
            IntPtr address,
            long offset,
            out byte value) {
            IntPtr target;
            value = 0;
            return TryAdd(address, offset, out target) && TryReadByte(peek, target, out value);
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

        private sealed class Snapshot : IEquatable<Snapshot> {
            internal Snapshot(
                IntPtr uiModule,
                IntPtr unitList,
                ushort count,
                IntPtr[] addons,
                int foundIndex = -1,
                uint visibility = 0,
                byte readiness = 0,
                bool isVisible = false,
                bool isReady = false,
                IntPtr numberData = default(IntPtr),
                int numberSize = 0,
                byte numberUpdate = 0,
                IntPtr numberValues = default(IntPtr),
                int visibleValue = 0,
                IntPtr stringData = default(IntPtr),
                int stringSize = 0,
                byte stringUpdate = 0,
                IntPtr stringValues = default(IntPtr),
                IntPtr namePointer = default(IntPtr),
                IntPtr textPointer = default(IntPtr)) {
                this.UiModule = uiModule;
                this.UnitList = unitList;
                this.Count = count;
                this.Addons = addons;
                this.FoundIndex = foundIndex;
                this.Visibility = visibility;
                this.Readiness = readiness;
                this.IsVisible = isVisible;
                this.IsReady = isReady;
                this.NumberData = numberData;
                this.NumberSize = numberSize;
                this.NumberUpdate = numberUpdate;
                this.NumberValues = numberValues;
                this.VisibleValue = visibleValue;
                this.StringData = stringData;
                this.StringSize = stringSize;
                this.StringUpdate = stringUpdate;
                this.StringValues = stringValues;
                this.NamePointer = namePointer;
                this.TextPointer = textPointer;
            }

            internal bool IsFound => this.FoundIndex >= 0;
            internal bool IsVisible { get; }
            internal bool IsReady { get; }
            internal IntPtr NamePointer { get; }
            internal IntPtr TextPointer { get; }
            private IntPtr UiModule { get; }
            private IntPtr UnitList { get; }
            private ushort Count { get; }
            private IntPtr[] Addons { get; }
            private int FoundIndex { get; }
            private uint Visibility { get; }
            private byte Readiness { get; }
            private IntPtr NumberData { get; }
            private int NumberSize { get; }
            private byte NumberUpdate { get; }
            private IntPtr NumberValues { get; }
            private int VisibleValue { get; }
            private IntPtr StringData { get; }
            private int StringSize { get; }
            private byte StringUpdate { get; }
            private IntPtr StringValues { get; }

            public bool Equals(Snapshot other) {
                if (other == null
                    || this.UiModule != other.UiModule
                    || this.UnitList != other.UnitList
                    || this.Count != other.Count
                    || this.FoundIndex != other.FoundIndex
                    || this.Visibility != other.Visibility
                    || this.Readiness != other.Readiness
                    || this.IsVisible != other.IsVisible
                    || this.IsReady != other.IsReady
                    || this.NumberData != other.NumberData
                    || this.NumberSize != other.NumberSize
                    || this.NumberUpdate != other.NumberUpdate
                    || this.NumberValues != other.NumberValues
                    || this.VisibleValue != other.VisibleValue
                    || this.StringData != other.StringData
                    || this.StringSize != other.StringSize
                    || this.StringUpdate != other.StringUpdate
                    || this.StringValues != other.StringValues
                    || this.NamePointer != other.NamePointer
                    || this.TextPointer != other.TextPointer
                    || this.Addons.Length != other.Addons.Length) {
                    return false;
                }

                for (int index = 0; index < this.Addons.Length; index++) {
                    if (this.Addons[index] != other.Addons[index]) return false;
                }

                return true;
            }
        }
    }
}
