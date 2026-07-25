namespace Sharlayan.Tests.Resources {
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Sharlayan.Models.ReadResults;
    using Sharlayan.Resources;

    using Xunit;

    public sealed class CurrentTalkMemoryReaderTests {
        [Fact]
        public void ReadsVisibleCurrentTalkFromStableAddonSnapshot() {
            SyntheticTalkMemory memory = SyntheticTalkMemory.Create();

            TalkResult result = CurrentTalkMemoryReader.Read(memory.Peek, memory.UiModulePointerAddress, memory.Layout);

            Assert.True(result.IsAvailable);
            Assert.True(result.IsVisible);
            Assert.Equal(TalkSource.Current, result.Source);
            Assert.Equal("Thancred", result.Name);
            Assert.Equal("Krile was of the same mind.", result.Text);
        }

        [Fact]
        public void RejectsHiddenUnreadyOrWrongAtkValueType() {
            SyntheticTalkMemory hidden = SyntheticTalkMemory.Create();
            hidden.WriteUInt32(hidden.Addon + hidden.Layout.VisibilityStateOffset, 0);
            SyntheticTalkMemory unready = SyntheticTalkMemory.Create();
            unready.WriteByte(unready.Addon + unready.Layout.ReadinessOffset, 0);
            SyntheticTalkMemory wrongType = SyntheticTalkMemory.Create();
            wrongType.WriteInt32(wrongType.AtkValues + wrongType.Layout.AtkValueTypeOffset, 3);

            Assert.False(CurrentTalkMemoryReader.Read(hidden.Peek, hidden.UiModulePointerAddress, hidden.Layout).IsAvailable);
            Assert.False(CurrentTalkMemoryReader.Read(unready.Peek, unready.UiModulePointerAddress, unready.Layout).IsAvailable);
            Assert.False(CurrentTalkMemoryReader.Read(wrongType.Peek, wrongType.UiModulePointerAddress, wrongType.Layout).IsAvailable);
        }

        [Fact]
        public void RejectsCorruptCountInvalidUtf8AndUnterminatedText() {
            SyntheticTalkMemory corruptCount = SyntheticTalkMemory.Create();
            corruptCount.WriteUInt16(corruptCount.CountAddress, (ushort)(corruptCount.Layout.Capacity + 1));
            SyntheticTalkMemory invalidUtf8 = SyntheticTalkMemory.Create();
            invalidUtf8.WriteTerminatedBytes(invalidUtf8.TextString, new byte[] { 0xC3, 0x28 });
            SyntheticTalkMemory unterminated = SyntheticTalkMemory.Create();
            unterminated.Fill(unterminated.TextString, TalkMemoryReader.MaximumStringBytes, 0x41);

            Assert.False(CurrentTalkMemoryReader.Read(corruptCount.Peek, corruptCount.UiModulePointerAddress, corruptCount.Layout).IsAvailable);
            Assert.False(CurrentTalkMemoryReader.Read(invalidUtf8.Peek, invalidUtf8.UiModulePointerAddress, invalidUtf8.Layout).IsAvailable);
            Assert.False(CurrentTalkMemoryReader.Read(unterminated.Peek, unterminated.UiModulePointerAddress, unterminated.Layout).IsAvailable);
        }

        [Fact]
        public void RetriesOnceWhenAddonPointerChangesDuringRead() {
            SyntheticTalkMemory memory = SyntheticTalkMemory.Create();
            int entryReads = 0;
            bool Peek(IntPtr address, byte[] buffer, int count) {
                if (address == new IntPtr(memory.EntryAddress) && ++entryReads == 2) {
                    memory.WritePointer(memory.EntryAddress, 0);
                }

                return memory.Peek(address, buffer, count);
            }

            TalkResult result = CurrentTalkMemoryReader.Read(Peek, memory.UiModulePointerAddress, memory.Layout);

            Assert.False(result.IsAvailable);
        }

        private sealed class SyntheticTalkMemory {
            private const long UiModule = 0x100000;
            private readonly Dictionary<long, byte> _memory = new Dictionary<long, byte>();

            private SyntheticTalkMemory() {
                this.Layout = new CurrentTalkMemoryLayout {
                    RaptureAtkModuleOffset = 0x100,
                    RaptureAtkUnitManagerOffset = 0x200,
                    AllLoadedUnitsListOffset = 0x300,
                    EntriesOffset = 8,
                    CountOffset = 40,
                    Capacity = 4,
                    EntrySize = 8,
                    AddonNameOffset = 8,
                    AddonNameCapacity = 32,
                    VisibilityStateOffset = 0x50,
                    VisibilityMask = 0x20,
                    ReadinessOffset = 0x54,
                    ReadinessMask = 1,
                    AtkValuesPointerOffset = 0x58,
                    AtkValuesCountOffset = 0x60,
                    AtkValueSize = 16,
                    AtkValueTypeOffset = 0,
                    AtkValueValueOffset = 8,
                    AllowedStringTypes = new[] { 0x28 },
                    AddonName = "Talk",
                    TextValueIndex = 0,
                    NameValueIndex = 1,
                };
                this.UiModulePointerAddress = new IntPtr(0x1000);
                this.Addon = 0x200000;
                this.AtkValues = 0x300000;
                this.TextString = 0x400000;
                this.NameString = 0x500000;
                long unitList = UiModule
                                + this.Layout.RaptureAtkModuleOffset
                                + this.Layout.RaptureAtkUnitManagerOffset
                                + this.Layout.AllLoadedUnitsListOffset;
                this.EntryAddress = unitList + this.Layout.EntriesOffset;
                this.CountAddress = unitList + this.Layout.CountOffset;

                this.WritePointer(this.UiModulePointerAddress.ToInt64(), UiModule);
                this.WriteUInt16(this.CountAddress, 1);
                this.WritePointer(this.EntryAddress, this.Addon);
                this.WriteFixedString(this.Addon + this.Layout.AddonNameOffset, this.Layout.AddonNameCapacity, "Talk");
                this.WriteUInt32(this.Addon + this.Layout.VisibilityStateOffset, this.Layout.VisibilityMask);
                this.WriteByte(this.Addon + this.Layout.ReadinessOffset, 1);
                this.WritePointer(this.Addon + this.Layout.AtkValuesPointerOffset, this.AtkValues);
                this.WriteUInt16(this.Addon + this.Layout.AtkValuesCountOffset, 2);
                this.WriteAtkString(0, this.TextString, "Krile was of the same mind.");
                this.WriteAtkString(1, this.NameString, "Thancred");
            }

            internal CurrentTalkMemoryLayout Layout { get; }
            internal IntPtr UiModulePointerAddress { get; }
            internal long Addon { get; }
            internal long AtkValues { get; }
            internal long TextString { get; }
            internal long NameString { get; }
            internal long EntryAddress { get; }
            internal long CountAddress { get; }

            internal static SyntheticTalkMemory Create() {
                return new SyntheticTalkMemory();
            }

            internal bool Peek(IntPtr address, byte[] buffer, int count) {
                long start = address.ToInt64();
                for (int index = 0; index < count; index++) {
                    if (!this._memory.TryGetValue(start + index, out byte value)) return false;
                    buffer[index] = value;
                }

                return true;
            }

            internal void Fill(long address, int count, byte value) {
                for (int index = 0; index < count; index++) this._memory[address + index] = value;
            }

            internal void WriteByte(long address, byte value) {
                this._memory[address] = value;
            }

            internal void WriteInt32(long address, int value) {
                this.Write(address, BitConverter.GetBytes(value));
            }

            internal void WriteUInt16(long address, ushort value) {
                this.Write(address, BitConverter.GetBytes(value));
            }

            internal void WriteUInt32(long address, uint value) {
                this.Write(address, BitConverter.GetBytes(value));
            }

            internal void WritePointer(long address, long value) {
                this.Write(address, BitConverter.GetBytes(value));
            }

            internal void WriteTerminatedBytes(long address, byte[] value) {
                this.Write(address, value);
                this.WriteByte(address + value.Length, 0);
            }

            private void WriteAtkString(int index, long stringAddress, string value) {
                long atkValue = this.AtkValues + (index * this.Layout.AtkValueSize);
                this.WriteInt32(atkValue + this.Layout.AtkValueTypeOffset, 0x28);
                this.WritePointer(atkValue + this.Layout.AtkValueValueOffset, stringAddress);
                this.WriteTerminatedBytes(stringAddress, Encoding.UTF8.GetBytes(value));
            }

            private void WriteFixedString(long address, int capacity, string value) {
                byte[] bytes = new byte[capacity];
                Encoding.ASCII.GetBytes(value, 0, value.Length, bytes, 0);
                this.Write(address, bytes);
            }

            private void Write(long address, byte[] value) {
                for (int index = 0; index < value.Length; index++) this._memory[address + index] = value[index];
            }
        }
    }
}
