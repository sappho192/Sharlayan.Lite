// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Reader.ChatLog.cs" company="SyndicatedLife">
//   Copyright© 2007 - 2022 Ryan Wilson <syndicated.life@gmail.com> (https://syndicated.life/)
//   Licensed under the MIT license. See LICENSE.md in the solution root for full license information.
// </copyright>
// <summary>
//   Reader.ChatLog.cs Implementation
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sharlayan {
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    using Sharlayan.Core;
    using Sharlayan.Models.ReadResults;
    using Sharlayan.Utilities;

    public partial class Reader {
        private const string UNRESOLVED = "UNRESOLVED";

        private readonly object _chatLogLock = new object();

        public bool CanGetChatLog() {
            bool canRead = this._chatLogReader.CanRead;
            if (canRead) {
                // OTHER STUFF?
            }

            return canRead;
        }

        public ChatLogResult GetChatLog(int previousArrayIndex = 0, int previousOffset = 0) {
            ChatLogResult result = new ChatLogResult();

            if (!this.CanGetChatLog() || !this._chatLogReader.IsAttached) {
                return result;
            }

            List<byte[]> bufferList = new List<byte[]>();
            Exception readException = null;

            lock (this._chatLogLock) {
                this._chatLogReader.PreviousArrayIndex = previousArrayIndex;
                this._chatLogReader.PreviousOffset = previousOffset;

                IntPtr chatPointerAddress = this._chatLogReader.GetChatLogAddress();
                if (chatPointerAddress.ToInt64() <= 20) {
                    return result;
                }

                try {
                    this._chatLogReader.ChatLogPointers = this._chatLogReader.ReadPointers(chatPointerAddress);

                    long currentArrayIndex = (this._chatLogReader.ChatLogPointers.OffsetArrayPos - this._chatLogReader.ChatLogPointers.OffsetArrayStart) / 4;
                    if (currentArrayIndex > 0) {
                        if (this._chatLogReader.ChatLogFirstRun) {
                            this._chatLogReader.EnsureArrayIndexes();
                            this._chatLogReader.ChatLogFirstRun = false;
                            this._chatLogReader.PreviousOffset = this._chatLogReader.Indexes[(int) currentArrayIndex - 1];
                            this._chatLogReader.PreviousArrayIndex = (int) currentArrayIndex - 1;
                        }
                        else {
                            this._chatLogReader.EnsureArrayIndexes();
                            if (currentArrayIndex < this._chatLogReader.PreviousArrayIndex) {
                                IEnumerable<byte[]> bufferEntries = this._chatLogReader.ResolveEntries(this._chatLogReader.PreviousArrayIndex, 1000);
                                bufferList.AddRange(bufferEntries);
                                this._chatLogReader.PreviousOffset = 0;
                                this._chatLogReader.PreviousArrayIndex = 0;
                            }

                            if (this._chatLogReader.PreviousArrayIndex < currentArrayIndex) {
                                IEnumerable<byte[]> bufferEntries = this._chatLogReader.ResolveEntries(this._chatLogReader.PreviousArrayIndex, (int) currentArrayIndex);
                                bufferList.AddRange(bufferEntries);
                            }

                            this._chatLogReader.PreviousArrayIndex = (int) currentArrayIndex;
                        }
                    }
                }
                catch (Exception ex) {
                    readException = ex;
                }

                result.PreviousArrayIndex = this._chatLogReader.PreviousArrayIndex;
                result.PreviousOffset = this._chatLogReader.PreviousOffset;
            }

            if (readException != null) {
                this._chatLogReader.RaiseException(Logger, readException);
            }

            foreach (byte[] bytes in bufferList) {
                if (bytes.Length == 0) {
                    continue;
                }

                try {
                    ChatLogItem chatLogEntry = ChatEntry.Process(bytes);

                    // assign logged user for this instance to chatLogEntry
                    chatLogEntry.PlayerCharacterName = this._pcWorkerDelegate.CurrentUser?.Name ?? UNRESOLVED;

                    if (Regex.IsMatch(chatLogEntry.Combined, @"[\w\d]{4}::?.+")) {
                        result.ChatLogItems.Enqueue(chatLogEntry);
                    }
                }
                catch (Exception ex) {
                    this._chatLogReader.RaiseException(Logger, ex);
                }
            }

            return result;
        }
    }
}
