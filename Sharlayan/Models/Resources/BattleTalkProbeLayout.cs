namespace Sharlayan.Models.Resources {
    using System;
    using System.Collections.Generic;

    internal sealed class BattleTalkProbeLayout {
        public int SchemaVersion { get; set; }
        public string FcsCommit { get; set; }
        public BattleTalkProbeUiLayout Ui { get; set; }
        public BattleTalkProbeAddonLayout Addon { get; set; }
        public BattleTalkProbeArrayLayout Arrays { get; set; }
        public BattleTalkProbeAgentHudLayout AgentHud { get; set; }
        public HermesUtf8StringLayout Utf8String { get; set; }
    }

    internal sealed class BattleTalkProbeUiLayout {
        public int UiModuleOffset { get; set; }
        public int RaptureAtkModuleOffset { get; set; }
    }

    internal sealed class BattleTalkProbeAddonLayout {
        public int RaptureAtkUnitManagerOffset { get; set; }
        public int AllLoadedUnitsListOffset { get; set; }
        public HermesAtkUnitListLayout AtkUnitList { get; set; }
        public HermesAtkUnitBaseLayout AtkUnitBase { get; set; }
        public HermesAtkValueLayout AtkValue { get; set; }
        public string BattleTalkAddonName { get; set; }
    }

    internal sealed class BattleTalkProbeArrayLayout {
        public int AtkArrayDataHolderOffset { get; set; }
        public int NumberArrayCountOffset { get; set; }
        public int NumberArraysOffset { get; set; }
        public int StringArrayCountOffset { get; set; }
        public int StringArraysOffset { get; set; }
        public int ArraySizeOffset { get; set; }
        public int ArrayUpdateStateOffset { get; set; }
        public int NumberValuesOffset { get; set; }
        public int StringValuesOffset { get; set; }
        public int ManagedStringValuesOffset { get; set; }
        public int BattleTalkNumberArrayId { get; set; }
        public int BattleTalkStringArrayId { get; set; }
    }

    internal sealed class BattleTalkProbeAgentHudLayout {
        public int AgentModuleOffset { get; set; }
        public int AgentsOffset { get; set; }
        public int AgentsCapacity { get; set; }
        public int AgentEntrySize { get; set; }
        public int HudAgentId { get; set; }
        public int QueueOffset { get; set; }
        public int QueueCapacity { get; set; }
        public int QueueEntrySize { get; set; }
        public int IsPendingOffset { get; set; }
        public int StyleOffset { get; set; }
        public int NameOffset { get; set; }
        public int TextOffset { get; set; }
        public int ImageOffset { get; set; }
        public int SoundOffset { get; set; }
        public int EntityIdOffset { get; set; }
    }

    internal sealed class BattleTalkProbeObservation {
        internal BattleTalkProbeObservation(
            BattleTalkProbeAddonObservation addon,
            BattleTalkProbeNumberArrayObservation numberArray,
            BattleTalkProbeStringArrayObservation stringArray,
            IReadOnlyList<BattleTalkProbeQueueSlot> queueSlots,
            string fingerprint) {
            this.Addon = addon;
            this.NumberArray = numberArray;
            this.StringArray = stringArray;
            this.QueueSlots = queueSlots;
            this.Fingerprint = fingerprint;
        }

        internal BattleTalkProbeAddonObservation Addon { get; }
        internal BattleTalkProbeNumberArrayObservation NumberArray { get; }
        internal BattleTalkProbeStringArrayObservation StringArray { get; }
        internal IReadOnlyList<BattleTalkProbeQueueSlot> QueueSlots { get; }
        internal string Fingerprint { get; }
    }

    internal sealed class BattleTalkProbeAddonObservation {
        internal bool IsReadable { get; set; }
        internal bool IsFound { get; set; }
        internal bool IsVisible { get; set; }
        internal bool IsReady { get; set; }
        internal int AtkValueCount { get; set; }
        internal IReadOnlyDictionary<int, BattleTalkProbeString> Strings { get; set; } =
            new Dictionary<int, BattleTalkProbeString>();
    }

    internal sealed class BattleTalkProbeNumberArrayObservation {
        internal bool IsReadable { get; set; }
        internal byte UpdateState { get; set; }
        internal IReadOnlyList<int> Values { get; set; } = new int[0];
    }

    internal sealed class BattleTalkProbeStringArrayObservation {
        internal bool IsReadable { get; set; }
        internal byte UpdateState { get; set; }
        internal IReadOnlyDictionary<int, BattleTalkProbeString> Values { get; set; } =
            new Dictionary<int, BattleTalkProbeString>();
    }

    internal sealed class BattleTalkProbeQueueSlot {
        internal int Index { get; set; }
        internal bool IsReadable { get; set; }
        internal bool IsPending { get; set; }
        internal byte Style { get; set; }
        internal BattleTalkProbeString Name { get; set; }
        internal BattleTalkProbeString Text { get; set; }
        internal uint Image { get; set; }
        internal int Sound { get; set; }
        internal uint EntityId { get; set; }
    }

    internal sealed class BattleTalkProbeString {
        internal static BattleTalkProbeString Empty { get; } =
            new BattleTalkProbeString(IntPtr.Zero, 0, string.Empty);

        internal BattleTalkProbeString(IntPtr pointer, int utf8Length, string hash) {
            this.Pointer = pointer;
            this.Utf8Length = utf8Length;
            this.Hash = hash;
        }

        internal IntPtr Pointer { get; }
        internal int Utf8Length { get; }
        internal string Hash { get; }
    }
}
