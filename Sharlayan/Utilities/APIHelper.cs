// --------------------------------------------------------------------------------------------------------------------
// <copyright file="APIHelper.cs" company="SyndicatedLife">
//   Copyright© 2007 - 2022 Ryan Wilson <syndicated.life@gmail.com> (https://syndicated.life/)
//   Licensed under the MIT license. See LICENSE.md in the solution root for full license information.
// </copyright>
// <summary>
//   APIHelper.cs Implementation
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sharlayan.Utilities {
    using System;
    using System.Threading.Tasks;

    using Sharlayan.Models;
    using Sharlayan.Models.Structures;
    using Sharlayan.Resources;

    public static class APIHelper {
        [Obsolete("Chat signatures are embedded at build time. Use Signatures.Resolve instead.")]
        public static Task<Signature[]> GetSignatures(SharlayanConfiguration configuration) {
            return Task.FromResult(GeneratedChatResources.CreateSignatures());
        }

        [Obsolete("Chat structures are embedded at build time and initialized by MemoryHandler.")]
        public static Task<StructuresContainer> GetStructures(SharlayanConfiguration configuration) {
            return Task.FromResult(GeneratedChatResources.CreateStructures());
        }
    }
}
