namespace Sharlayan {
    using System;

    using Sharlayan.Models.ReadResults;
    using Sharlayan.Resources;

    public partial class Reader {
        private readonly object _battleTalkLock = new object();
        private readonly BattleTalkSequenceTracker _battleTalkTracker =
            new BattleTalkSequenceTracker();

        public bool CanGetBattleTalk() {
            return this._memoryHandler != null
                   && this._memoryHandler.BattleTalkLayout != null
                   && this._memoryHandler.Scanner.Locations.ContainsKey(
                       Signatures.CURRENT_TALK_UI_MODULE_POINTER_KEY);
        }

        public BattleTalkResult GetBattleTalk() {
            if (!this.CanGetBattleTalk() || !this._memoryHandler.IsAttached) {
                return new BattleTalkResult(
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    this._battleTalkTracker.Sequence);
            }

            lock (this._battleTalkLock) {
                try {
                    IntPtr uiModulePointerAddress =
                        this._memoryHandler.Scanner.Locations[
                            Signatures.CURRENT_TALK_UI_MODULE_POINTER_KEY];
                    BattleTalkMemoryObservation observation = BattleTalkMemoryReader.Read(
                        this._memoryHandler.Peek,
                        uiModulePointerAddress,
                        this._memoryHandler.BattleTalkLayout);
                    return this._battleTalkTracker.Observe(observation);
                }
                catch (Exception exception) {
                    this._memoryHandler.RaiseException(Logger, exception);
                    return new BattleTalkResult(
                        false,
                        false,
                        string.Empty,
                        string.Empty,
                        this._battleTalkTracker.Sequence);
                }
            }
        }
    }

    internal sealed class BattleTalkSequenceTracker {
        private bool wasVisible;
        private string name = string.Empty;
        private string text = string.Empty;

        internal long Sequence { get; private set; }

        internal BattleTalkResult Observe(BattleTalkMemoryObservation observation) {
            if (!observation.IsAvailable) {
                return new BattleTalkResult(
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    this.Sequence);
            }

            if (!observation.IsVisible) {
                this.wasVisible = false;
                return new BattleTalkResult(
                    true,
                    false,
                    string.Empty,
                    string.Empty,
                    this.Sequence);
            }

            if (!this.wasVisible
                || !string.Equals(this.name, observation.Name, StringComparison.Ordinal)
                || !string.Equals(this.text, observation.Text, StringComparison.Ordinal)) {
                this.Sequence = checked(this.Sequence + 1);
            }

            this.wasVisible = true;
            this.name = observation.Name;
            this.text = observation.Text;
            return new BattleTalkResult(
                true,
                true,
                observation.Name,
                observation.Text,
                this.Sequence);
        }
    }
}
