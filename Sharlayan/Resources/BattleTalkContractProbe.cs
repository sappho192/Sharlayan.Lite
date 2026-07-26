namespace Sharlayan.Resources {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;

    using Sharlayan.Models.Resources;

    internal static class BattleTalkContractProbe {
        private const int MaximumArraySize = 4096;
        private const int MaximumStringBytes = 65536;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static BattleTalkProbeObservation Read(
            MemoryPeek peek,
            IntPtr uiModulePointerAddress,
            BattleTalkProbeLayout layout) {
            if (peek == null) throw new ArgumentNullException(nameof(peek));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            Validate(layout);

            IntPtr uiModule;
            IntPtr raptureAtkModule;
            if (!TryReadPointer(peek, uiModulePointerAddress, out uiModule)
                || uiModule == IntPtr.Zero
                || !TryAdd(uiModule, layout.Ui.RaptureAtkModuleOffset, out raptureAtkModule)) {
                return EmptyObservation();
            }

            BattleTalkProbeAddonObservation addon = ReadAddon(peek, raptureAtkModule, layout.Addon);
            BattleTalkProbeNumberArrayObservation numberArray = ReadNumberArray(peek, raptureAtkModule, layout.Arrays);
            BattleTalkProbeStringArrayObservation stringArray = ReadStringArray(peek, raptureAtkModule, layout.Arrays);
            IReadOnlyList<BattleTalkProbeQueueSlot> queue = ReadQueue(peek, raptureAtkModule, layout.AgentHud, layout.Utf8String);
            string fingerprint = CalculateFingerprint(addon, numberArray, stringArray, queue);
            return new BattleTalkProbeObservation(addon, numberArray, stringArray, queue, fingerprint);
        }

        private static BattleTalkProbeAddonObservation ReadAddon(
            MemoryPeek peek,
            IntPtr raptureAtkModule,
            BattleTalkProbeAddonLayout layout) {
            BattleTalkProbeAddonObservation result = new BattleTalkProbeAddonObservation();
            IntPtr manager;
            IntPtr list;
            IntPtr countAddress;
            ushort count;
            IntPtr entries;
            if (!TryAdd(raptureAtkModule, layout.RaptureAtkUnitManagerOffset, out manager)
                || !TryAdd(manager, layout.AllLoadedUnitsListOffset, out list)
                || !TryAdd(list, layout.AtkUnitList.CountOffset, out countAddress)
                || !TryReadUInt16(peek, countAddress, out count)
                || count > layout.AtkUnitList.Capacity
                || !TryAdd(list, layout.AtkUnitList.EntriesOffset, out entries)) {
                return result;
            }

            result.IsReadable = true;
            for (int index = 0; index < count; index++) {
                IntPtr entry;
                IntPtr addon;
                if (!TryAdd(entries, (long) index * layout.AtkUnitList.EntrySize, out entry)
                    || !TryReadPointer(peek, entry, out addon)) {
                    result.IsReadable = false;
                    return result;
                }

                if (addon == IntPtr.Zero || !IsAddonNamed(peek, addon, layout.AtkUnitBase, layout.BattleTalkAddonName)) {
                    continue;
                }

                result.IsFound = true;
                uint visibility;
                byte readiness;
                IntPtr visibilityAddress;
                IntPtr readinessAddress;
                IntPtr countValueAddress;
                ushort atkValueCount;
                IntPtr valuesPointerAddress;
                IntPtr values;
                if (!TryAdd(addon, layout.AtkUnitBase.VisibilityStateOffset, out visibilityAddress)
                    || !TryReadUInt32(peek, visibilityAddress, out visibility)
                    || !TryAdd(addon, layout.AtkUnitBase.ReadinessOffset, out readinessAddress)
                    || !TryReadByte(peek, readinessAddress, out readiness)
                    || !TryAdd(addon, layout.AtkUnitBase.AtkValuesCountOffset, out countValueAddress)
                    || !TryReadUInt16(peek, countValueAddress, out atkValueCount)
                    || atkValueCount > MaximumArraySize
                    || !TryAdd(addon, layout.AtkUnitBase.AtkValuesPointerOffset, out valuesPointerAddress)
                    || !TryReadPointer(peek, valuesPointerAddress, out values)) {
                    result.IsReadable = false;
                    return result;
                }

                result.IsVisible = (visibility & layout.AtkUnitBase.VisibilityMask) != 0;
                result.IsReady = (readiness & layout.AtkUnitBase.ReadinessMask) != 0;
                result.AtkValueCount = atkValueCount;
                Dictionary<int, BattleTalkProbeString> strings = new Dictionary<int, BattleTalkProbeString>();
                int captureCount = Math.Min((int) atkValueCount, 128);
                for (int valueIndex = 0; valueIndex < captureCount; valueIndex++) {
                    IntPtr valueAddress;
                    IntPtr typeAddress;
                    int type;
                    IntPtr pointerAddress;
                    IntPtr pointer;
                    if (!TryAdd(values, (long) valueIndex * layout.AtkValue.Size, out valueAddress)
                        || !TryAdd(valueAddress, layout.AtkValue.TypeOffset, out typeAddress)
                        || !TryReadInt32(peek, typeAddress, out type)
                        || !layout.AtkValue.AllowedStringTypes.Contains(type)
                        || !TryAdd(valueAddress, layout.AtkValue.ValueOffset, out pointerAddress)
                        || !TryReadPointer(peek, pointerAddress, out pointer)
                        || pointer == IntPtr.Zero) {
                        continue;
                    }

                    BattleTalkProbeString value;
                    if (TryReadCString(peek, pointer, out value)) {
                        strings[valueIndex] = value;
                    }
                }

                result.Strings = strings;
                return result;
            }

            return result;
        }

        private static BattleTalkProbeNumberArrayObservation ReadNumberArray(
            MemoryPeek peek,
            IntPtr raptureAtkModule,
            BattleTalkProbeArrayLayout layout) {
            BattleTalkProbeNumberArrayObservation result = new BattleTalkProbeNumberArrayObservation();
            IntPtr holder;
            short count;
            IntPtr arrays;
            IntPtr data;
            int size;
            byte updateState;
            IntPtr values;
            if (!TryAdd(raptureAtkModule, layout.AtkArrayDataHolderOffset, out holder)
                || !TryReadInt16(peek, holder, layout.NumberArrayCountOffset, out count)
                || count <= layout.BattleTalkNumberArrayId
                || !TryReadPointerAt(peek, holder, layout.NumberArraysOffset, out arrays)
                || !TryReadPointerAt(peek, arrays, (long) layout.BattleTalkNumberArrayId * 8, out data)
                || data == IntPtr.Zero
                || !TryReadInt32At(peek, data, layout.ArraySizeOffset, out size)
                || size < 0
                || size > MaximumArraySize
                || !TryReadByteAt(peek, data, layout.ArrayUpdateStateOffset, out updateState)
                || !TryReadPointerAt(peek, data, layout.NumberValuesOffset, out values)) {
                return result;
            }

            byte[] bytes = new byte[checked(size * 4)];
            if (size > 0 && (values == IntPtr.Zero || !peek(values, bytes, bytes.Length))) {
                return result;
            }

            int[] integers = new int[size];
            for (int index = 0; index < size; index++) {
                integers[index] = BitConverter.ToInt32(bytes, index * 4);
            }

            result.IsReadable = true;
            result.UpdateState = updateState;
            result.Values = integers;
            return result;
        }

        private static BattleTalkProbeStringArrayObservation ReadStringArray(
            MemoryPeek peek,
            IntPtr raptureAtkModule,
            BattleTalkProbeArrayLayout layout) {
            BattleTalkProbeStringArrayObservation result = new BattleTalkProbeStringArrayObservation();
            IntPtr holder;
            short count;
            IntPtr arrays;
            IntPtr data;
            int size;
            byte updateState;
            IntPtr values;
            if (!TryAdd(raptureAtkModule, layout.AtkArrayDataHolderOffset, out holder)
                || !TryReadInt16(peek, holder, layout.StringArrayCountOffset, out count)
                || count <= layout.BattleTalkStringArrayId
                || !TryReadPointerAt(peek, holder, layout.StringArraysOffset, out arrays)
                || !TryReadPointerAt(peek, arrays, (long) layout.BattleTalkStringArrayId * 8, out data)
                || data == IntPtr.Zero
                || !TryReadInt32At(peek, data, layout.ArraySizeOffset, out size)
                || size < 0
                || size > MaximumArraySize
                || !TryReadByteAt(peek, data, layout.ArrayUpdateStateOffset, out updateState)
                || !TryReadPointerAt(peek, data, layout.StringValuesOffset, out values)) {
                return result;
            }

            Dictionary<int, BattleTalkProbeString> strings = new Dictionary<int, BattleTalkProbeString>();
            for (int index = 0; index < size; index++) {
                IntPtr pointer;
                if (!TryReadPointerAt(peek, values, (long) index * 8, out pointer)) {
                    return result;
                }

                BattleTalkProbeString value;
                if (pointer != IntPtr.Zero && TryReadCString(peek, pointer, out value)) {
                    strings[index] = value;
                }
            }

            result.IsReadable = true;
            result.UpdateState = updateState;
            result.Values = strings;
            return result;
        }

        private static IReadOnlyList<BattleTalkProbeQueueSlot> ReadQueue(
            MemoryPeek peek,
            IntPtr raptureAtkModule,
            BattleTalkProbeAgentHudLayout layout,
            HermesUtf8StringLayout utf8String) {
            List<BattleTalkProbeQueueSlot> slots = new List<BattleTalkProbeQueueSlot>();
            IntPtr agentModule;
            IntPtr agents;
            IntPtr hud;
            IntPtr queue;
            if (layout.HudAgentId < 0
                || layout.HudAgentId >= layout.AgentsCapacity
                || !TryAdd(raptureAtkModule, layout.AgentModuleOffset, out agentModule)
                || !TryAdd(agentModule, layout.AgentsOffset, out agents)
                || !TryReadPointerAt(peek, agents, (long) layout.HudAgentId * layout.AgentEntrySize, out hud)
                || hud == IntPtr.Zero
                || !TryAdd(hud, layout.QueueOffset, out queue)) {
                return slots;
            }

            for (int index = 0; index < layout.QueueCapacity; index++) {
                BattleTalkProbeQueueSlot slot = new BattleTalkProbeQueueSlot { Index = index };
                IntPtr entry;
                bool pending;
                byte style;
                uint image;
                int sound;
                uint entity;
                BattleTalkProbeString name;
                BattleTalkProbeString text;
                if (TryAdd(queue, (long) index * layout.QueueEntrySize, out entry)
                    && TryReadBooleanAt(peek, entry, layout.IsPendingOffset, out pending)
                    && TryReadByteAt(peek, entry, layout.StyleOffset, out style)
                    && TryReadUtf8StringAt(peek, entry, layout.NameOffset, utf8String, out name)
                    && TryReadUtf8StringAt(peek, entry, layout.TextOffset, utf8String, out text)
                    && TryReadUInt32At(peek, entry, layout.ImageOffset, out image)
                    && TryReadInt32At(peek, entry, layout.SoundOffset, out sound)
                    && TryReadUInt32At(peek, entry, layout.EntityIdOffset, out entity)) {
                    slot.IsReadable = true;
                    slot.IsPending = pending;
                    slot.Style = style;
                    slot.Name = name;
                    slot.Text = text;
                    slot.Image = image;
                    slot.Sound = sound;
                    slot.EntityId = entity;
                }

                slots.Add(slot);
            }

            return slots;
        }

        private static bool TryReadUtf8StringAt(
            MemoryPeek peek,
            IntPtr entry,
            int offset,
            HermesUtf8StringLayout layout,
            out BattleTalkProbeString value) {
            value = BattleTalkProbeString.Empty;
            IntPtr address;
            IntPtr pointer;
            long used;
            if (!TryAdd(entry, offset, out address)
                || !TryReadPointerAt(peek, address, layout.StringPointerOffset, out pointer)
                || !TryReadInt64At(peek, address, layout.BufferUsedOffset, out used)) {
                return false;
            }

            if (used == 0 && pointer == IntPtr.Zero) {
                value = BattleTalkProbeString.Empty;
                return true;
            }

            if (used < 1 || used > MaximumStringBytes + 1 || pointer == IntPtr.Zero) return false;

            int byteCount = checked((int) used);
            byte[] bytes = new byte[byteCount];
            if (!peek(pointer, bytes, byteCount) || bytes[byteCount - 1] != 0) {
                return false;
            }

            return TryCreateString(pointer, bytes, byteCount - 1, out value);
        }

        private static bool TryReadCString(MemoryPeek peek, IntPtr pointer, out BattleTalkProbeString value) {
            value = BattleTalkProbeString.Empty;
            List<byte> bytes = new List<byte>();
            byte[] chunk = new byte[256];
            for (int offset = 0; offset < MaximumStringBytes; offset += chunk.Length) {
                IntPtr address;
                int count = Math.Min(chunk.Length, MaximumStringBytes - offset);
                if (!TryAdd(pointer, offset, out address) || !peek(address, chunk, count)) {
                    return false;
                }

                for (int index = 0; index < count; index++) {
                    if (chunk[index] == 0) {
                        return TryCreateString(pointer, bytes.ToArray(), bytes.Count, out value);
                    }

                    bytes.Add(chunk[index]);
                }
            }

            return false;
        }

        private static bool TryCreateString(
            IntPtr pointer,
            byte[] bytes,
            int count,
            out BattleTalkProbeString value) {
            value = BattleTalkProbeString.Empty;
            try {
                StrictUtf8.GetString(bytes, 0, count);
                string hash;
                using (SHA256 sha256 = SHA256.Create()) {
                    hash = ToLowerHex(sha256.ComputeHash(bytes, 0, count)).Substring(0, 16);
                }

                value = new BattleTalkProbeString(pointer, count, hash);
                return true;
            }
            catch (DecoderFallbackException) {
                return false;
            }
        }

        private static bool IsAddonNamed(
            MemoryPeek peek,
            IntPtr addon,
            HermesAtkUnitBaseLayout layout,
            string expectedName) {
            IntPtr address;
            if (!TryAdd(addon, layout.NameOffset, out address)) return false;
            byte[] bytes = new byte[layout.NameCapacity];
            if (!peek(address, bytes, bytes.Length)) return false;
            byte[] expected = Encoding.ASCII.GetBytes(expectedName);
            if (expected.Length >= bytes.Length || bytes[expected.Length] != 0) return false;
            for (int index = 0; index < expected.Length; index++) {
                if (bytes[index] != expected[index]) return false;
            }

            return true;
        }

        private static string CalculateFingerprint(
            BattleTalkProbeAddonObservation addon,
            BattleTalkProbeNumberArrayObservation number,
            BattleTalkProbeStringArrayObservation strings,
            IReadOnlyList<BattleTalkProbeQueueSlot> queue) {
            StringBuilder builder = new StringBuilder();
            builder.Append(addon.IsReadable).Append('|').Append(addon.IsFound).Append('|')
                .Append(addon.IsVisible).Append('|').Append(addon.IsReady).Append('|')
                .Append(addon.AtkValueCount);
            AppendStrings(builder, addon.Strings);
            builder.Append('|').Append(number.IsReadable).Append('|').Append(number.UpdateState);
            foreach (int item in number.Values) builder.Append(',').Append(item);
            builder.Append('|').Append(strings.IsReadable).Append('|').Append(strings.UpdateState);
            AppendStrings(builder, strings.Values);
            foreach (BattleTalkProbeQueueSlot slot in queue) {
                builder.Append('|').Append(slot.Index).Append(':').Append(slot.IsReadable).Append(':')
                    .Append(slot.IsPending).Append(':').Append(slot.Style).Append(':')
                    .Append(slot.Name?.Hash).Append(':').Append(slot.Text?.Hash).Append(':')
                    .Append(slot.Image).Append(':').Append(slot.Sound).Append(':').Append(slot.EntityId);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
            using (SHA256 sha256 = SHA256.Create()) {
                return ToLowerHex(sha256.ComputeHash(bytes));
            }
        }

        private static string ToLowerHex(byte[] bytes) {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void AppendStrings(
            StringBuilder builder,
            IReadOnlyDictionary<int, BattleTalkProbeString> strings) {
            foreach (KeyValuePair<int, BattleTalkProbeString> pair in strings) {
                builder.Append('|').Append(pair.Key).Append(':').Append(pair.Value.Pointer.ToInt64())
                    .Append(':').Append(pair.Value.Utf8Length).Append(':').Append(pair.Value.Hash);
            }
        }

        private static BattleTalkProbeObservation EmptyObservation() {
            BattleTalkProbeAddonObservation addon = new BattleTalkProbeAddonObservation();
            BattleTalkProbeNumberArrayObservation number = new BattleTalkProbeNumberArrayObservation();
            BattleTalkProbeStringArrayObservation strings = new BattleTalkProbeStringArrayObservation();
            return new BattleTalkProbeObservation(addon, number, strings, new BattleTalkProbeQueueSlot[0], string.Empty);
        }

        private static void Validate(BattleTalkProbeLayout layout) {
            if (layout.SchemaVersion != 1
                || layout.Ui == null
                || layout.Addon == null
                || layout.Arrays == null
                || layout.AgentHud == null
                || layout.Utf8String == null
                || layout.Addon.AtkUnitList == null
                || layout.Addon.AtkUnitBase == null
                || layout.Addon.AtkValue == null
                || layout.Addon.AtkValue.AllowedStringTypes == null
                || layout.Addon.AtkUnitList.Capacity <= 0
                || layout.Addon.AtkUnitList.Capacity > MaximumArraySize
                || layout.AgentHud.QueueCapacity <= 0
                || layout.AgentHud.QueueCapacity > 64
                || layout.AgentHud.QueueEntrySize <= 0) {
                throw new InvalidDataException("BattleTalk probe layout is invalid.");
            }
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

        private static bool TryReadPointer(MemoryPeek peek, IntPtr address, out IntPtr value) {
            byte[] bytes = new byte[8];
            if (!peek(address, bytes, bytes.Length)) {
                value = IntPtr.Zero;
                return false;
            }

            value = new IntPtr(BitConverter.ToInt64(bytes, 0));
            return true;
        }

        private static bool TryReadPointerAt(MemoryPeek peek, IntPtr address, long offset, out IntPtr value) {
            IntPtr target;
            value = IntPtr.Zero;
            return TryAdd(address, offset, out target) && TryReadPointer(peek, target, out value);
        }

        private static bool TryReadBooleanAt(MemoryPeek peek, IntPtr address, long offset, out bool value) {
            byte raw;
            bool success = TryReadByteAt(peek, address, offset, out raw);
            value = raw != 0;
            return success;
        }

        private static bool TryReadByte(MemoryPeek peek, IntPtr address, out byte value) {
            byte[] bytes = new byte[1];
            if (!peek(address, bytes, 1)) {
                value = 0;
                return false;
            }

            value = bytes[0];
            return true;
        }

        private static bool TryReadByteAt(MemoryPeek peek, IntPtr address, long offset, out byte value) {
            IntPtr target;
            value = 0;
            return TryAdd(address, offset, out target) && TryReadByte(peek, target, out value);
        }

        private static bool TryReadInt16(MemoryPeek peek, IntPtr address, long offset, out short value) {
            IntPtr target;
            byte[] bytes = new byte[2];
            if (!TryAdd(address, offset, out target) || !peek(target, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = BitConverter.ToInt16(bytes, 0);
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

        private static bool TryReadInt32(MemoryPeek peek, IntPtr address, out int value) {
            byte[] bytes = new byte[4];
            if (!peek(address, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = BitConverter.ToInt32(bytes, 0);
            return true;
        }

        private static bool TryReadInt32At(MemoryPeek peek, IntPtr address, long offset, out int value) {
            IntPtr target;
            value = 0;
            return TryAdd(address, offset, out target) && TryReadInt32(peek, target, out value);
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

        private static bool TryReadUInt32At(MemoryPeek peek, IntPtr address, long offset, out uint value) {
            IntPtr target;
            value = 0;
            return TryAdd(address, offset, out target) && TryReadUInt32(peek, target, out value);
        }

        private static bool TryReadInt64At(MemoryPeek peek, IntPtr address, long offset, out long value) {
            IntPtr target;
            byte[] bytes = new byte[8];
            if (!TryAdd(address, offset, out target) || !peek(target, bytes, bytes.Length)) {
                value = 0;
                return false;
            }

            value = BitConverter.ToInt64(bytes, 0);
            return true;
        }
    }
}
