namespace Sharlayan.Tests.Resources {
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Sharlayan.Models.Resources;
    using Sharlayan.Resources;

    using Xunit;

    public sealed class BattleTalkContractProbeTests {
        [Fact]
        public void CapturesAddonArraysAndQueueWithoutExposingText() {
            SyntheticBattleTalkMemory memory = SyntheticBattleTalkMemory.Create();

            BattleTalkProbeObservation result =
                BattleTalkContractProbe.Read(memory.Peek, memory.UiModulePointerAddress, memory.Layout);

            Assert.True(result.Addon.IsReadable);
            Assert.True(result.Addon.IsFound);
            Assert.True(result.Addon.IsVisible);
            Assert.True(result.Addon.IsReady);
            Assert.Single(result.Addon.Strings);
            Assert.Equal(2, result.NumberArray.Values.Count);
            Assert.Equal(17, result.NumberArray.Values[0]);
            Assert.Single(result.StringArray.Values);
            Assert.Equal(2, result.QueueSlots.Count);
            Assert.True(result.QueueSlots[0].IsReadable);
            Assert.True(result.QueueSlots[0].IsPending);
            Assert.Equal(6, result.QueueSlots[0].Name.Utf8Length);
            Assert.Equal(13, result.QueueSlots[0].Text.Utf8Length);
            Assert.DoesNotContain("Ysayle", result.Fingerprint);
            Assert.DoesNotContain("Hold the line", result.Fingerprint);
        }

        [Fact]
        public void MarksCorruptQueueSlotUnreadableAndRejectsInvalidLayout() {
            SyntheticBattleTalkMemory memory = SyntheticBattleTalkMemory.Create();
            memory.WriteBytes(memory.QueueName, new byte[] { 0xC3, 0x28, 0 });

            BattleTalkProbeObservation result =
                BattleTalkContractProbe.Read(memory.Peek, memory.UiModulePointerAddress, memory.Layout);

            Assert.False(result.QueueSlots[0].IsReadable);

            memory.Layout.AgentHud.QueueCapacity = 0;
            Assert.Throws<System.IO.InvalidDataException>(
                () => BattleTalkContractProbe.Read(memory.Peek, memory.UiModulePointerAddress, memory.Layout));
        }

        private sealed class SyntheticBattleTalkMemory {
            private const long UiModule = 0x10000;
            private readonly Dictionary<long, byte> memory = new Dictionary<long, byte>();

            private SyntheticBattleTalkMemory() {
                this.Layout = CreateLayout();
                this.UiModulePointerAddress = new IntPtr(0x1000);
                long raptureAtkModule = UiModule + this.Layout.Ui.RaptureAtkModuleOffset;
                long list = raptureAtkModule
                            + this.Layout.Addon.RaptureAtkUnitManagerOffset
                            + this.Layout.Addon.AllLoadedUnitsListOffset;
                long addon = 0x20000;
                long atkValues = 0x21000;
                long addonText = 0x22000;
                long holder = raptureAtkModule + this.Layout.Arrays.AtkArrayDataHolderOffset;
                long numberPointers = 0x30000;
                long numberData = 0x31000;
                long numberValues = 0x32000;
                long stringPointers = 0x33000;
                long stringData = 0x34000;
                long stringValues = 0x35000;
                long stringText = 0x36000;
                long agentModule = raptureAtkModule + this.Layout.AgentHud.AgentModuleOffset;
                long agents = agentModule + this.Layout.AgentHud.AgentsOffset;
                long hud = 0x40000;
                long queue = hud + this.Layout.AgentHud.QueueOffset;
                this.QueueName = 0x41000;
                this.QueueText = 0x42000;

                this.WritePointer(this.UiModulePointerAddress.ToInt64(), UiModule);
                this.WriteUInt16(list + this.Layout.Addon.AtkUnitList.CountOffset, 1);
                this.WritePointer(list + this.Layout.Addon.AtkUnitList.EntriesOffset, addon);
                this.WriteFixedString(addon + this.Layout.Addon.AtkUnitBase.NameOffset, 32, "_BattleTalk");
                this.WriteUInt32(
                    addon + this.Layout.Addon.AtkUnitBase.VisibilityStateOffset,
                    this.Layout.Addon.AtkUnitBase.VisibilityMask);
                this.WriteByte(addon + this.Layout.Addon.AtkUnitBase.ReadinessOffset, 1);
                this.WritePointer(addon + this.Layout.Addon.AtkUnitBase.AtkValuesPointerOffset, atkValues);
                this.WriteUInt16(addon + this.Layout.Addon.AtkUnitBase.AtkValuesCountOffset, 1);
                this.WriteInt32(atkValues, 0x28);
                this.WritePointer(atkValues + 8, addonText);
                this.WriteCString(addonText, "Hold the line");

                this.WriteInt16(holder + this.Layout.Arrays.NumberArrayCountOffset, 2);
                this.WritePointer(holder + this.Layout.Arrays.NumberArraysOffset, numberPointers);
                this.WritePointer(numberPointers + 8, numberData);
                this.WriteInt32(numberData + this.Layout.Arrays.ArraySizeOffset, 2);
                this.WriteByte(numberData + this.Layout.Arrays.ArrayUpdateStateOffset, 3);
                this.WritePointer(numberData + this.Layout.Arrays.NumberValuesOffset, numberValues);
                this.WriteInt32(numberValues, 17);
                this.WriteInt32(numberValues + 4, 23);

                this.WriteInt16(holder + this.Layout.Arrays.StringArrayCountOffset, 2);
                this.WritePointer(holder + this.Layout.Arrays.StringArraysOffset, stringPointers);
                this.WritePointer(stringPointers + 8, stringData);
                this.WriteInt32(stringData + this.Layout.Arrays.ArraySizeOffset, 1);
                this.WriteByte(stringData + this.Layout.Arrays.ArrayUpdateStateOffset, 4);
                this.WritePointer(stringData + this.Layout.Arrays.StringValuesOffset, stringValues);
                this.WritePointer(stringValues, stringText);
                this.WriteCString(stringText, "Hold the line");

                this.WritePointer(agents + 8, hud);
                this.WriteQueueSlot(queue, true, this.QueueName, "Ysayle", this.QueueText, "Hold the line");
                this.WriteQueueSlot(
                    queue + this.Layout.AgentHud.QueueEntrySize,
                    false,
                    0x43000,
                    string.Empty,
                    0x44000,
                    string.Empty);
            }

            internal BattleTalkProbeLayout Layout { get; }
            internal IntPtr UiModulePointerAddress { get; }
            internal long QueueName { get; }
            internal long QueueText { get; }

            internal static SyntheticBattleTalkMemory Create() => new SyntheticBattleTalkMemory();

            internal bool Peek(IntPtr address, byte[] buffer, int count) {
                for (int index = 0; index < count; index++) {
                    if (!this.memory.TryGetValue(address.ToInt64() + index, out byte value)) return false;
                    buffer[index] = value;
                }

                return true;
            }

            internal void WriteBytes(long address, byte[] bytes) {
                for (int index = 0; index < bytes.Length; index++) this.memory[address + index] = bytes[index];
            }

            private static BattleTalkProbeLayout CreateLayout() {
                return new BattleTalkProbeLayout {
                    SchemaVersion = 1,
                    FcsCommit = "fixture",
                    Ui = new BattleTalkProbeUiLayout { RaptureAtkModuleOffset = 0x100 },
                    Addon = new BattleTalkProbeAddonLayout {
                        RaptureAtkUnitManagerOffset = 0x200,
                        AllLoadedUnitsListOffset = 0x300,
                        AtkUnitList = new HermesAtkUnitListLayout {
                            EntriesOffset = 8,
                            CountOffset = 0x20,
                            Capacity = 4,
                            EntrySize = 8,
                        },
                        AtkUnitBase = new HermesAtkUnitBaseLayout {
                            NameOffset = 8,
                            NameCapacity = 32,
                            VisibilityStateOffset = 0x40,
                            VisibilityMask = 0x20,
                            ReadinessOffset = 0x44,
                            ReadinessMask = 1,
                            AtkValuesPointerOffset = 0x48,
                            AtkValuesCountOffset = 0x50,
                        },
                        AtkValue = new HermesAtkValueLayout {
                            Size = 16,
                            TypeOffset = 0,
                            ValueOffset = 8,
                            AllowedStringTypes = new List<int> { 0x28 },
                        },
                        BattleTalkAddonName = "_BattleTalk",
                    },
                    Arrays = new BattleTalkProbeArrayLayout {
                        AtkArrayDataHolderOffset = 0x400,
                        NumberArrayCountOffset = 0,
                        NumberArraysOffset = 8,
                        StringArrayCountOffset = 2,
                        StringArraysOffset = 16,
                        ArraySizeOffset = 0,
                        ArrayUpdateStateOffset = 4,
                        NumberValuesOffset = 8,
                        StringValuesOffset = 8,
                        BattleTalkNumberArrayId = 1,
                        BattleTalkStringArrayId = 1,
                    },
                    AgentHud = new BattleTalkProbeAgentHudLayout {
                        AgentModuleOffset = 0x500,
                        AgentsOffset = 8,
                        AgentsCapacity = 4,
                        AgentEntrySize = 8,
                        HudAgentId = 1,
                        QueueOffset = 0x100,
                        QueueCapacity = 2,
                        QueueEntrySize = 0x80,
                        IsPendingOffset = 0,
                        StyleOffset = 1,
                        NameOffset = 8,
                        TextOffset = 40,
                        ImageOffset = 72,
                        SoundOffset = 76,
                        EntityIdOffset = 80,
                    },
                    Utf8String = new HermesUtf8StringLayout {
                        StringPointerOffset = 0,
                        BufferUsedOffset = 16,
                    },
                };
            }

            private void WriteQueueSlot(
                long entry,
                bool pending,
                long namePointer,
                string name,
                long textPointer,
                string text) {
                this.WriteByte(entry, pending ? (byte) 1 : (byte) 0);
                this.WriteByte(entry + 1, 2);
                this.WriteUtf8String(entry + 8, namePointer, name);
                this.WriteUtf8String(entry + 40, textPointer, text);
                this.WriteUInt32(entry + 72, 101);
                this.WriteInt32(entry + 76, 202);
                this.WriteUInt32(entry + 80, 303);
            }

            private void WriteUtf8String(long address, long pointer, string value) {
                byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
                this.WritePointer(address, pointer);
                this.WriteInt64(address + 16, bytes.Length);
                this.WriteBytes(pointer, bytes);
            }

            private void WriteCString(long address, string value) {
                byte[] bytes = new byte[256];
                Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
                this.WriteBytes(address, bytes);
            }

            private void WriteFixedString(long address, int capacity, string value) {
                byte[] bytes = new byte[capacity];
                Encoding.ASCII.GetBytes(value, 0, value.Length, bytes, 0);
                this.WriteBytes(address, bytes);
            }

            private void WriteByte(long address, byte value) => this.memory[address] = value;
            private void WriteInt16(long address, short value) => this.WriteBytes(address, BitConverter.GetBytes(value));
            private void WriteUInt16(long address, ushort value) => this.WriteBytes(address, BitConverter.GetBytes(value));
            private void WriteInt32(long address, int value) => this.WriteBytes(address, BitConverter.GetBytes(value));
            private void WriteUInt32(long address, uint value) => this.WriteBytes(address, BitConverter.GetBytes(value));
            private void WriteInt64(long address, long value) => this.WriteBytes(address, BitConverter.GetBytes(value));
            private void WritePointer(long address, long value) => this.WriteInt64(address, value);
        }
    }
}
