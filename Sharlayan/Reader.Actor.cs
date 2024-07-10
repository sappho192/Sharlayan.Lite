// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Reader.Actor.cs" company="SyndicatedLife">
//   Copyright© 2007 - 2022 Ryan Wilson <syndicated.life@gmail.com> (https://syndicated.life/)
//   Licensed under the MIT license. See LICENSE.md in the solution root for full license information.
// </copyright>
// <summary>
//   Reader.Actor.cs Implementation
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sharlayan {
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    using Sharlayan.Core;
    using Sharlayan.Core.Enums;
    using Sharlayan.Models.ReadResults;
    using Sharlayan.Utilities;

    public partial class Reader {
        private ConcurrentDictionary<uint, DateTime> _expiringActors = new ConcurrentDictionary<uint, DateTime>();

        private ConcurrentDictionary<IntPtr, IntPtr> _uniqueCharacterAddresses = new ConcurrentDictionary<IntPtr, IntPtr>();

        public bool CanGetActors() {
            bool canRead = this._memoryHandler.Scanner.Locations.ContainsKey(Signatures.CHARMAP_KEY);
            if (canRead) {
                // OTHER STUFF?
            }

            return canRead;
        }
    }
}