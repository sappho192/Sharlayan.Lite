namespace Sharlayan.Tests.Resources {
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Sharlayan.Resources;

    using Xunit;

    public sealed class BattleTalkMemoryReaderTests {
        [Fact]
        public void ReadsVisibleBattleTalkAndReportsStableHiddenOrAbsent() {
            SyntheticBattleTalkMemory visible = SyntheticBattleTalkMemory.Create();
            SyntheticBattleTalkMemory hidden = SyntheticBattleTalkMemory.Create();
            hidden.WriteUInt32(hidden.Addon + hidden.Layout.VisibilityStateOffset, 0);
            SyntheticBattleTalkMemory absent = SyntheticBattleTalkMemory.Create();
            absent.WriteUInt16(absent.CountAddress, 0);

            BattleTalkMemoryObservation visibleResult =
                BattleTalkMemoryReader.Read(visible.Peek, visible.UiModulePointerAddress, visible.Layout);
            BattleTalkMemoryObservation hiddenResult =
                BattleTalkMemoryReader.Read(hidden.Peek, hidden.UiModulePointerAddress, hidden.Layout);
            BattleTalkMemoryObservation absentResult =
                BattleTalkMemoryReader.Read(absent.Peek, absent.UiModulePointerAddress, absent.Layout);

            Assert.True(visibleResult.IsAvailable);
            Assert.True(visibleResult.IsVisible);
            Assert.Equal("Ysayle", visibleResult.Name);
            Assert.Equal("Hold the line.", visibleResult.Text);
            Assert.True(hiddenResult.IsAvailable);
            Assert.False(hiddenResult.IsVisible);
            Assert.True(absentResult.IsAvailable);
            Assert.False(absentResult.IsVisible);
        }

        [Fact]
        public void RejectsVisibilityDisagreementInvalidUtf8AndOversizedArray() {
            SyntheticBattleTalkMemory disagreement = SyntheticBattleTalkMemory.Create();
            disagreement.WriteInt32(disagreement.NumberValues, 0);
            SyntheticBattleTalkMemory invalidUtf8 = SyntheticBattleTalkMemory.Create();
            invalidUtf8.WriteCStringBytes(invalidUtf8.TextString, new byte[] { 0xC3, 0x28 });
            SyntheticBattleTalkMemory oversized = SyntheticBattleTalkMemory.Create();
            oversized.WriteInt32(oversized.StringData + oversized.Layout.ArraySizeOffset, 4097);

            Assert.False(
                BattleTalkMemoryReader.Read(
                    disagreement.Peek,
                    disagreement.UiModulePointerAddress,
                    disagreement.Layout).IsAvailable);
            Assert.False(
                BattleTalkMemoryReader.Read(
                    invalidUtf8.Peek,
                    invalidUtf8.UiModulePointerAddress,
                    invalidUtf8.Layout).IsAvailable);
            Assert.False(
                BattleTalkMemoryReader.Read(
                    oversized.Peek,
                    oversized.UiModulePointerAddress,
                    oversized.Layout).IsAvailable);
        }

        [Fact]
        public void SequenceChangesOnContentOrVisibilityGeneration() {
            BattleTalkSequenceTracker tracker = new BattleTalkSequenceTracker();
            BattleTalkMemoryObservation first =
                new BattleTalkMemoryObservation(true, true, "Ysayle", "Hold the line.");
            BattleTalkMemoryObservation second =
                new BattleTalkMemoryObservation(true, true, "Ysayle", "Advance.");

            Assert.Equal(1, tracker.Observe(first).Sequence);
            Assert.Equal(1, tracker.Observe(first).Sequence);
            Assert.Equal(2, tracker.Observe(second).Sequence);
            tracker.Observe(BattleTalkMemoryObservation.Hidden);
            Assert.Equal(3, tracker.Observe(first).Sequence);
        }

        private sealed class SyntheticBattleTalkMemory {
            private const long UiModule = 0x10000;
            private readonly Dictionary<long, byte> memory = new Dictionary<long, byte>();

            private SyntheticBattleTalkMemory() {
                this.Layout = new BattleTalkMemoryLayout {
                    RaptureAtkModuleOffset = 0x100,
                    RaptureAtkUnitManagerOffset = 0x200,
                    AllLoadedUnitsListOffset = 0x300,
                    EntriesOffset = 8,
                    CountOffset = 0x20,
                    Capacity = 4,
                    EntrySize = 8,
                    AddonNameOffset = 8,
                    AddonNameCapacity = 32,
                    VisibilityStateOffset = 0x40,
                    VisibilityMask = 0x20,
                    ReadinessOffset = 0x44,
                    ReadinessMask = 1,
                    AddonName = "_BattleTalk",
                    AtkArrayDataHolderOffset = 0x400,
                    NumberArrayCountOffset = 0,
                    NumberArraysOffset = 8,
                    StringArrayCountOffset = 2,
                    StringArraysOffset = 16,
                    ArraySizeOffset = 0,
                    ArrayUpdateStateOffset = 4,
                    NumberValuesOffset = 8,
                    StringValuesOffset = 8,
                    NumberArrayId = 0,
                    StringArrayId = 0,
                    VisibleIndex = 0,
                    NameIndex = 0,
                    TextIndex = 1,
                };
                this.UiModulePointerAddress = new IntPtr(0x1000);
                long rapture = UiModule + this.Layout.RaptureAtkModuleOffset;
                long list = rapture
                            + this.Layout.RaptureAtkUnitManagerOffset
                            + this.Layout.AllLoadedUnitsListOffset;
                this.CountAddress = list + this.Layout.CountOffset;
                this.Addon = 0x20000;
                long holder = rapture + this.Layout.AtkArrayDataHolderOffset;
                long numberArrays = 0x30000;
                long numberData = 0x31000;
                this.NumberValues = 0x32000;
                long stringArrays = 0x33000;
                this.StringData = 0x34000;
                long stringValues = 0x35000;
                this.NameString = 0x36000;
                this.TextString = 0x37000;

                this.WritePointer(this.UiModulePointerAddress.ToInt64(), UiModule);
                this.WriteUInt16(this.CountAddress, 1);
                this.WritePointer(list + this.Layout.EntriesOffset, this.Addon);
                this.WriteFixedString(this.Addon + this.Layout.AddonNameOffset, 32, "_BattleTalk");
                this.WriteUInt32(this.Addon + this.Layout.VisibilityStateOffset, 0x20);
                this.WriteByte(this.Addon + this.Layout.ReadinessOffset, 1);

                this.WriteInt16(holder, 1);
                this.WritePointer(holder + 8, numberArrays);
                this.WritePointer(numberArrays, numberData);
                this.WriteInt32(numberData, 1);
                this.WriteByte(numberData + 4, 0);
                this.WritePointer(numberData + 8, this.NumberValues);
                this.WriteInt32(this.NumberValues, 7);

                this.WriteInt16(holder + 2, 1);
                this.WritePointer(holder + 16, stringArrays);
                this.WritePointer(stringArrays, this.StringData);
                this.WriteInt32(this.StringData, 2);
                this.WriteByte(this.StringData + 4, 0);
                this.WritePointer(this.StringData + 8, stringValues);
                this.WritePointer(stringValues, this.NameString);
                this.WritePointer(stringValues + 8, this.TextString);
                this.WriteCString(this.NameString, "Ysayle");
                this.WriteCString(this.TextString, "Hold the line.");
            }

            internal BattleTalkMemoryLayout Layout { get; }
            internal IntPtr UiModulePointerAddress { get; }
            internal long CountAddress { get; }
            internal long Addon { get; }
            internal long NumberValues { get; }
            internal long StringData { get; }
            internal long NameString { get; }
            internal long TextString { get; }

            internal static SyntheticBattleTalkMemory Create() => new SyntheticBattleTalkMemory();

            internal bool Peek(IntPtr address, byte[] buffer, int count) {
                for (int index = 0; index < count; index++) {
                    if (!this.memory.TryGetValue(address.ToInt64() + index, out byte value)) return false;
                    buffer[index] = value;
                }

                return true;
            }

            internal void WriteUInt16(long address, ushort value) =>
                this.Write(address, BitConverter.GetBytes(value));

            internal void WriteUInt32(long address, uint value) =>
                this.Write(address, BitConverter.GetBytes(value));

            internal void WriteInt32(long address, int value) =>
                this.Write(address, BitConverter.GetBytes(value));

            internal void WriteCStringBytes(long address, byte[] value) {
                byte[] bytes = new byte[256];
                Buffer.BlockCopy(value, 0, bytes, 0, value.Length);
                this.Write(address, bytes);
            }

            private void WriteInt16(long address, short value) =>
                this.Write(address, BitConverter.GetBytes(value));

            private void WritePointer(long address, long value) =>
                this.Write(address, BitConverter.GetBytes(value));

            private void WriteByte(long address, byte value) => this.memory[address] = value;

            private void WriteCString(long address, string value) =>
                this.WriteCStringBytes(address, Encoding.UTF8.GetBytes(value));

            private void WriteFixedString(long address, int capacity, string value) {
                byte[] bytes = new byte[capacity];
                Encoding.ASCII.GetBytes(value, 0, value.Length, bytes, 0);
                this.Write(address, bytes);
            }

            private void Write(long address, byte[] bytes) {
                for (int index = 0; index < bytes.Length; index++) this.memory[address + index] = bytes[index];
            }
        }
    }
}
