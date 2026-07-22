namespace Sharlayan {
    using System;

    using Sharlayan.Models.ReadResults;
    using Sharlayan.Resources;

    public partial class Reader {
        private readonly object _talkLock = new object();

        public bool CanGetLastTalk() {
            return this._memoryHandler != null
                   && this._memoryHandler.TalkLayout != null
                   && this._memoryHandler.Scanner.Locations.ContainsKey(Signatures.LAST_TALK_NAME_KEY)
                   && this._memoryHandler.Scanner.Locations.ContainsKey(Signatures.LAST_TALK_TEXT_KEY);
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
    }
}
