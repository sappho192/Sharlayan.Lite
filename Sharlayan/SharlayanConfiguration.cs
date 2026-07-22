// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SharlayanConfiguration.cs" company="SyndicatedLife">
//   Copyright© 2007 - 2022 Ryan Wilson <syndicated.life@gmail.com> (https://syndicated.life/)
//   Licensed under the MIT license. See LICENSE.md in the solution root for full license information.
// </copyright>
// <summary>
//   SharlayanConfiguration.cs Implementation
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sharlayan {
    using System;
    using System.IO;

    using Sharlayan.Enums;
    using Sharlayan.Models;

    public class SharlayanConfiguration {
        [Obsolete("Chat resources are embedded at build time; APIBaseURL is no longer used.")]
        public string APIBaseURL { get; set; } = Constants.DEFAULT_API_BASE_URL;
        public string CharacterName { get; set; }
        public GameLanguage GameLanguage { get; set; } = GameLanguage.English;
        [Obsolete("Chat resources are region-independent; GameRegion is no longer used.")]
        public GameRegion GameRegion { get; set; } = GameRegion.Global;
        [Obsolete("Chat resources are embedded at build time; JSONCacheDirectory is no longer used.")]
        public string JSONCacheDirectory { get; set; } = Directory.GetCurrentDirectory();
        [Obsolete("Chat resources are versioned with the package; PatchVersion is no longer used.")]
        public string PatchVersion { get; set; } = "latest";
        public ProcessModel ProcessModel { get; set; }
        public bool ScanAllRegions { get; set; } = false;
        [Obsolete("Chat resources are embedded at build time; UseLocalCache is no longer used.")]
        public bool UseLocalCache { get; set; } = true;
    }
}
