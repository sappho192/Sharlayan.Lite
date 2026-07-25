namespace Sharlayan {
    using System;

    using Sharlayan.Models.ReadResults;
    using Sharlayan.Resources;

    public partial class Reader {
        private readonly object _talkLock = new object();

        public bool CanGetCurrentTalk() {
            return this._memoryHandler != null
                   && this._memoryHandler.CurrentTalkLayout != null
                   && this._memoryHandler.Scanner.Locations.ContainsKey(Signatures.CURRENT_TALK_UI_MODULE_POINTER_KEY);
        }

        public bool CanGetLastTalk() {
            return this._memoryHandler != null
                   && this._memoryHandler.TalkLayout != null
                   && this._memoryHandler.Scanner.Locations.ContainsKey(Signatures.LAST_TALK_NAME_KEY)
                   && this._memoryHandler.Scanner.Locations.ContainsKey(Signatures.LAST_TALK_TEXT_KEY);
        }

        public bool CanGetTalk() {
            return this.CanGetCurrentTalk() || this.CanGetLastTalk();
        }

        public TalkResult GetCurrentTalk() {
            if (!this.CanGetCurrentTalk() || !this._memoryHandler.IsAttached) {
                return TalkResult.Unavailable;
            }

            lock (this._talkLock) {
                try {
                    IntPtr uiModulePointerAddress =
                        this._memoryHandler.Scanner.Locations[Signatures.CURRENT_TALK_UI_MODULE_POINTER_KEY];
                    return CurrentTalkMemoryReader.Read(
                        this._memoryHandler.Peek,
                        uiModulePointerAddress,
                        this._memoryHandler.CurrentTalkLayout);
                }
                catch (Exception ex) {
                    this._memoryHandler.RaiseException(Logger, ex);
                    return TalkResult.Unavailable;
                }
            }
        }

        public TalkResult GetLastTalk() {
            if (!this.CanGetLastTalk() || !this._memoryHandler.IsAttached) {
                return TalkResult.Unavailable;
            }

            lock (this._talkLock) {
                try {
                    IntPtr nameAddress = this._memoryHandler.Scanner.Locations[Signatures.LAST_TALK_NAME_KEY];
                    IntPtr textAddress = this._memoryHandler.Scanner.Locations[Signatures.LAST_TALK_TEXT_KEY];
                    return TalkMemoryReader.Read(this._memoryHandler.Peek, nameAddress, textAddress, this._memoryHandler.TalkLayout);
                }
                catch (Exception ex) {
                    this._memoryHandler.RaiseException(Logger, ex);
                    return TalkResult.Unavailable;
                }
            }
        }

        public TalkResult GetTalk() {
            TalkResult current = this.GetCurrentTalk();
            return current.IsAvailable ? current : this.GetLastTalk();
        }
    }
}
