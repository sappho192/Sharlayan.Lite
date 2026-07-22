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
            ChatLogResult result = new ChatLogResult {
                PreviousArrayIndex = previousArrayIndex,
                PreviousOffset = previousOffset,
            };

            if (!this.CanGetChatLog() || !this._chatLogReader.IsAttached) {
                return result;
            }

            List<byte[]> bufferList = new List<byte[]>();
            Exception readException = null;

            lock (this._chatLogLock) {
                int resolvedArrayIndex = previousArrayIndex;
                int resolvedOffset = previousOffset;
                bool firstRun = this._chatLogReader.ChatLogFirstRun;

                IntPtr chatPointerAddress = this._chatLogReader.GetChatLogAddress();
                if (chatPointerAddress.ToInt64() <= 20) {
                    readException = new InvalidOperationException($"Invalid chat pointer address: 0x{chatPointerAddress.ToInt64():X}.");
                }

                try {
                    if (readException != null) {
                        throw readException;
                    }

                    this._chatLogReader.ChatLogPointers = this._chatLogReader.ReadPointers(chatPointerAddress);

                    int arrayCapacity = this._chatLogReader.GetArrayCapacity(out int currentArrayIndex);
                    int logCapacity = this._chatLogReader.GetLogCapacity();
                    this._chatLogReader.EnsureArrayIndexes(arrayCapacity);
                    bool cursorInRange = resolvedArrayIndex >= 0 && resolvedArrayIndex <= arrayCapacity && resolvedOffset >= 0 && resolvedOffset <= logCapacity;
                    bool cursorMatches = cursorInRange && this._chatLogReader.IsCursorBoundary(resolvedArrayIndex, resolvedOffset);
                    if (!firstRun && this._chatLogReader.HasPointerVectorChanged(arrayCapacity) && !cursorMatches) {
                        firstRun = true;
                    }
                    else if (!firstRun && !cursorMatches) {
                        throw new InvalidOperationException($"Invalid previous chat cursor: index={resolvedArrayIndex}, offset={resolvedOffset}.");
                    }

                    if (firstRun) {
                        firstRun = false;
                        resolvedOffset = currentArrayIndex > 0 ? this._chatLogReader.Indexes[currentArrayIndex - 1] : 0;
                        resolvedArrayIndex = currentArrayIndex;
                        if (resolvedOffset < 0 || resolvedOffset > this._chatLogReader.GetLogPosition()) {
                            throw new InvalidOperationException($"Invalid initial chat cursor offset: {resolvedOffset}.");
                        }
                    }
                    else {
                        if (currentArrayIndex < resolvedArrayIndex) {
                            bufferList.AddRange(this._chatLogReader.ResolveEntries(resolvedArrayIndex, arrayCapacity, resolvedOffset, logCapacity, out resolvedOffset));
                            resolvedOffset = 0;
                            resolvedArrayIndex = 0;
                        }

                        if (resolvedArrayIndex < currentArrayIndex) {
                            bufferList.AddRange(this._chatLogReader.ResolveEntries(resolvedArrayIndex, currentArrayIndex, resolvedOffset, this._chatLogReader.GetLogPosition(), out resolvedOffset));
                        }

                        resolvedArrayIndex = currentArrayIndex;
                    }

                    this._chatLogReader.ChatLogFirstRun = firstRun;
                    this._chatLogReader.PreviousArrayIndex = resolvedArrayIndex;
                    this._chatLogReader.PreviousOffset = resolvedOffset;
                    this._chatLogReader.RememberPointerVector(arrayCapacity);
                }
                catch (Exception ex) {
                    readException = ex;
                    bufferList.Clear();
                }

                if (readException == null) {
                    result.PreviousArrayIndex = this._chatLogReader.PreviousArrayIndex;
                    result.PreviousOffset = this._chatLogReader.PreviousOffset;
                }
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

                    string characterName = this._characterName();
                    chatLogEntry.PlayerCharacterName = string.IsNullOrWhiteSpace(characterName) ? UNRESOLVED : characterName;

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
