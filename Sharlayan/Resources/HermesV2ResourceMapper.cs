namespace Sharlayan.Resources {
    using System;
    using System.Collections.Generic;

    using Sharlayan.Models;
    using Sharlayan.Models.Resources;
    using Sharlayan.Models.Structures;
    using ChatLogPointersStructure = Sharlayan.Models.Structures.ChatLogPointers;

    internal sealed class HermesMappedResources {
        internal HermesMappedResources(
            Signature[] signatures,
            StructuresContainer structures,
            TalkMemoryLayout talkLayout,
            CurrentTalkMemoryLayout currentTalkLayout,
            BattleTalkMemoryLayout battleTalkLayout) {
            this.Signatures = signatures;
            this.Structures = structures;
            this.TalkLayout = talkLayout;
            this.CurrentTalkLayout = currentTalkLayout;
            this.BattleTalkLayout = battleTalkLayout;
        }

        internal Signature[] Signatures { get; }
        internal StructuresContainer Structures { get; }
        internal TalkMemoryLayout TalkLayout { get; }
        internal CurrentTalkMemoryLayout CurrentTalkLayout { get; }
        internal BattleTalkMemoryLayout BattleTalkLayout { get; }
    }

    internal sealed class TalkMemoryLayout {
        internal TalkMemoryLayout(int stringPointerOffset, int bufferUsedOffset) {
            this.StringPointerOffset = stringPointerOffset;
            this.BufferUsedOffset = bufferUsedOffset;
            this.HeaderSize = Math.Max(stringPointerOffset + 8, bufferUsedOffset + 8);
        }

        internal int StringPointerOffset { get; }
        internal int BufferUsedOffset { get; }
        internal int HeaderSize { get; }
    }

    internal sealed class CurrentTalkMemoryLayout {
        internal int RaptureAtkModuleOffset { get; set; }
        internal int RaptureAtkUnitManagerOffset { get; set; }
        internal int AllLoadedUnitsListOffset { get; set; }
        internal int EntriesOffset { get; set; }
        internal int CountOffset { get; set; }
        internal int Capacity { get; set; }
        internal int EntrySize { get; set; }
        internal int AddonNameOffset { get; set; }
        internal int AddonNameCapacity { get; set; }
        internal int VisibilityStateOffset { get; set; }
        internal uint VisibilityMask { get; set; }
        internal int ReadinessOffset { get; set; }
        internal uint ReadinessMask { get; set; }
        internal int AtkValuesPointerOffset { get; set; }
        internal int AtkValuesCountOffset { get; set; }
        internal int AtkValueSize { get; set; }
        internal int AtkValueTypeOffset { get; set; }
        internal int AtkValueValueOffset { get; set; }
        internal int[] AllowedStringTypes { get; set; }
        internal string AddonName { get; set; }
        internal int TextValueIndex { get; set; }
        internal int NameValueIndex { get; set; }
    }

    internal sealed class BattleTalkMemoryLayout {
        internal int RaptureAtkModuleOffset { get; set; }
        internal int RaptureAtkUnitManagerOffset { get; set; }
        internal int AllLoadedUnitsListOffset { get; set; }
        internal int EntriesOffset { get; set; }
        internal int CountOffset { get; set; }
        internal int Capacity { get; set; }
        internal int EntrySize { get; set; }
        internal int AddonNameOffset { get; set; }
        internal int AddonNameCapacity { get; set; }
        internal int VisibilityStateOffset { get; set; }
        internal uint VisibilityMask { get; set; }
        internal int ReadinessOffset { get; set; }
        internal uint ReadinessMask { get; set; }
        internal string AddonName { get; set; }
        internal int AtkArrayDataHolderOffset { get; set; }
        internal int NumberArrayCountOffset { get; set; }
        internal int NumberArraysOffset { get; set; }
        internal int StringArrayCountOffset { get; set; }
        internal int StringArraysOffset { get; set; }
        internal int ArraySizeOffset { get; set; }
        internal int ArrayUpdateStateOffset { get; set; }
        internal int NumberValuesOffset { get; set; }
        internal int StringValuesOffset { get; set; }
        internal int NumberArrayId { get; set; }
        internal int StringArrayId { get; set; }
        internal int VisibleIndex { get; set; }
        internal int NameIndex { get; set; }
        internal int TextIndex { get; set; }
    }

    internal static class HermesV2ResourceMapper {
        internal static HermesMappedResources Map(HermesV2Manifest manifest) {
            HermesFrameworkRoot root = manifest.Roots.Framework;
            HermesChatLogResource chat = manifest.Resources.ChatLog;
            HermesTalkResource talk = manifest.Resources.Talk;
            HermesCurrentTalkResource currentTalk = manifest.Resources.CurrentTalk;
            int patternLength = root.Pattern.Length / 2;
            long rewindOffset = -(patternLength - root.RelativeFollowOffset);

            Signature[] signatures = {
                CreateSignature(Signatures.CHATLOG_KEY, root.Pattern, rewindOffset, chat.UiModuleOffset, chat.RaptureLogModuleOffset),
                CreateSignature(Signatures.LAST_TALK_NAME_KEY, root.Pattern, rewindOffset, talk.UiModuleOffset, talk.NameOffset),
                CreateSignature(Signatures.LAST_TALK_TEXT_KEY, root.Pattern, rewindOffset, talk.UiModuleOffset, talk.TextOffset),
                new Signature {
                    ASMSignature = true,
                    Key = Signatures.CURRENT_TALK_UI_MODULE_POINTER_KEY,
                    PointerPath = new List<long> { rewindOffset, 0, currentTalk.UiModuleOffset },
                    Value = root.Pattern,
                },
            };
            StructuresContainer structures = new StructuresContainer {
                ChatLogPointers = new ChatLogPointersStructure {
                    OffsetArrayStart = chat.IndexVectorOffset,
                    OffsetArrayPos = chat.IndexVectorOffset + 8,
                    OffsetArrayEnd = chat.IndexVectorOffset + 16,
                    LogStart = chat.DataVectorOffset,
                    LogNext = chat.DataVectorOffset + 8,
                    LogEnd = chat.DataVectorOffset + 16,
                },
            };
            TalkMemoryLayout layout = new TalkMemoryLayout(
                talk.Utf8String.StringPointerOffset,
                talk.Utf8String.BufferUsedOffset);
            CurrentTalkMemoryLayout currentLayout = new CurrentTalkMemoryLayout {
                RaptureAtkModuleOffset = currentTalk.RaptureAtkModuleOffset,
                RaptureAtkUnitManagerOffset = currentTalk.RaptureAtkUnitManagerOffset,
                AllLoadedUnitsListOffset = currentTalk.AllLoadedUnitsListOffset,
                EntriesOffset = currentTalk.AtkUnitList.EntriesOffset,
                CountOffset = currentTalk.AtkUnitList.CountOffset,
                Capacity = currentTalk.AtkUnitList.Capacity,
                EntrySize = currentTalk.AtkUnitList.EntrySize,
                AddonNameOffset = currentTalk.AtkUnitBase.NameOffset,
                AddonNameCapacity = currentTalk.AtkUnitBase.NameCapacity,
                VisibilityStateOffset = currentTalk.AtkUnitBase.VisibilityStateOffset,
                VisibilityMask = currentTalk.AtkUnitBase.VisibilityMask,
                ReadinessOffset = currentTalk.AtkUnitBase.ReadinessOffset,
                ReadinessMask = currentTalk.AtkUnitBase.ReadinessMask,
                AtkValuesPointerOffset = currentTalk.AtkUnitBase.AtkValuesPointerOffset,
                AtkValuesCountOffset = currentTalk.AtkUnitBase.AtkValuesCountOffset,
                AtkValueSize = currentTalk.AtkValue.Size,
                AtkValueTypeOffset = currentTalk.AtkValue.TypeOffset,
                AtkValueValueOffset = currentTalk.AtkValue.ValueOffset,
                AllowedStringTypes = currentTalk.AtkValue.AllowedStringTypes.ToArray(),
                AddonName = currentTalk.AddonName,
                TextValueIndex = currentTalk.TextValueIndex,
                NameValueIndex = currentTalk.NameValueIndex,
            };
            BattleTalkMemoryLayout battleLayout = null;
            HermesBattleTalkResource battle = manifest.Resources.BattleTalk;
            if (battle != null) {
                battleLayout = new BattleTalkMemoryLayout {
                    RaptureAtkModuleOffset = battle.RaptureAtkModuleOffset,
                    RaptureAtkUnitManagerOffset = battle.RaptureAtkUnitManagerOffset,
                    AllLoadedUnitsListOffset = battle.AllLoadedUnitsListOffset,
                    EntriesOffset = battle.AtkUnitList.EntriesOffset,
                    CountOffset = battle.AtkUnitList.CountOffset,
                    Capacity = battle.AtkUnitList.Capacity,
                    EntrySize = battle.AtkUnitList.EntrySize,
                    AddonNameOffset = battle.Addon.NameOffset,
                    AddonNameCapacity = battle.Addon.NameCapacity,
                    VisibilityStateOffset = battle.Addon.VisibilityStateOffset,
                    VisibilityMask = battle.Addon.VisibilityMask,
                    ReadinessOffset = battle.Addon.ReadinessOffset,
                    ReadinessMask = battle.Addon.ReadinessMask,
                    AddonName = battle.AddonName,
                    AtkArrayDataHolderOffset = battle.AtkArrayDataHolderOffset,
                    NumberArrayCountOffset = battle.ArrayDataHolder.NumberArrayCountOffset,
                    NumberArraysOffset = battle.ArrayDataHolder.NumberArraysOffset,
                    StringArrayCountOffset = battle.ArrayDataHolder.StringArrayCountOffset,
                    StringArraysOffset = battle.ArrayDataHolder.StringArraysOffset,
                    ArraySizeOffset = battle.ArrayData.SizeOffset,
                    ArrayUpdateStateOffset = battle.ArrayData.UpdateStateOffset,
                    NumberValuesOffset = battle.NumberValuesOffset,
                    StringValuesOffset = battle.StringValuesOffset,
                    NumberArrayId = battle.NumberArrayId,
                    StringArrayId = battle.StringArrayId,
                    VisibleIndex = battle.VisibleIndex,
                    NameIndex = battle.NameIndex,
                    TextIndex = battle.TextIndex,
                };
            }

            return new HermesMappedResources(
                signatures,
                structures,
                layout,
                currentLayout,
                battleLayout);
        }

        private static Signature CreateSignature(string key, string pattern, long rewindOffset, int uiModuleOffset, int resourceOffset) {
            return new Signature {
                ASMSignature = true,
                Key = key,
                PointerPath = new List<long> { rewindOffset, 0, uiModuleOffset, resourceOffset },
                Value = pattern,
            };
        }
    }
}
