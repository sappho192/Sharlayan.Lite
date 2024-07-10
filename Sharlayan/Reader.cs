// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Reader.cs" company="SyndicatedLife">
//   Copyright© 2007 - 2022 Ryan Wilson <syndicated.life@gmail.com> (https://syndicated.life/)
//   Licensed under the MIT license. See LICENSE.md in the solution root for full license information.
// </copyright>
// <summary>
//   Reader.cs Implementation
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sharlayan {
    using Sharlayan.Delegates;
    using Sharlayan.Utilities;

    public partial class Reader {
        private ChatLogReader _chatLogReader;

        private ChatLogWorkerDelegate _chatLogWorkerDelegate = new ChatLogWorkerDelegate();

        private PCWorkerDelegate _pcWorkerDelegate = new PCWorkerDelegate();

        public Reader(MemoryHandler memoryHandler) {
            this._memoryHandler = memoryHandler;

            this._chatLogReader = new ChatLogReader(this._memoryHandler);

            this._chatLogWorkerDelegate = new ChatLogWorkerDelegate();
            this._pcWorkerDelegate = new PCWorkerDelegate();
        }

        private MemoryHandler _memoryHandler { get; }
    }
}