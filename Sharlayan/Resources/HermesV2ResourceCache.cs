namespace Sharlayan.Resources {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    internal sealed class HermesV2ResourceCache {
        private static readonly Regex Revision = new Regex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private readonly string _root;
        private readonly string _manifests;

        internal HermesV2ResourceCache(string cacheDirectory) {
            if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
                this._root = Path.Combine(Path.GetFullPath(cacheDirectory), "hermes-v2");
                this._manifests = Path.Combine(this._root, "manifests");
            }
        }

        internal bool IsEnabled => !string.IsNullOrEmpty(this._root);

        internal bool TryReadLatest(out byte[] bytes, out string etag) {
            bytes = null;
            etag = null;
            if (!this.IsEnabled) return false;
            try {
                string latestPath = Path.Combine(this._root, "latest.json");
                if (!File.Exists(latestPath)) return false;
                bytes = File.ReadAllBytes(latestPath);
                string etagPath = Path.Combine(this._root, "latest.etag");
                etag = File.Exists(etagPath) ? File.ReadAllText(etagPath).Trim() : null;
                return true;
            }
            catch {
                return false;
            }
        }

        internal bool TryReadManifest(string revision, out byte[] bytes) {
            bytes = null;
            if (!this.IsEnabled || !Revision.IsMatch(revision ?? string.Empty)) return false;
            try {
                string path = Path.Combine(this._manifests, ManifestFileName(revision));
                if (!File.Exists(path)) return false;
                bytes = File.ReadAllBytes(path);
                return true;
            }
            catch {
                return false;
            }
        }

        internal IEnumerable<(string Revision, byte[] Bytes)> ReadManifestFallbacks() {
            if (!this.IsEnabled || !Directory.Exists(this._manifests)) yield break;
            FileInfo[] files;
            try {
                files = new DirectoryInfo(this._manifests).GetFiles("*.json")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToArray();
            }
            catch {
                yield break;
            }

            foreach (FileInfo file in files) {
                string revision = "sha256:" + Path.GetFileNameWithoutExtension(file.Name);
                if (!Revision.IsMatch(revision)) continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(file.FullName); }
                catch { continue; }
                yield return (revision, bytes);
            }
        }

        internal void WriteLatest(byte[] bytes, string etag) {
            if (!this.IsEnabled) return;
            try {
                Directory.CreateDirectory(this._root);
                WriteAtomic(Path.Combine(this._root, "latest.json"), bytes);
                if (!string.IsNullOrWhiteSpace(etag)) {
                    WriteAtomic(Path.Combine(this._root, "latest.etag"), Encoding.UTF8.GetBytes(etag));
                }
                else {
                    string etagPath = Path.Combine(this._root, "latest.etag");
                    if (File.Exists(etagPath)) File.Delete(etagPath);
                }
            }
            catch {
                // Cache failures do not invalidate a verified remote manifest.
            }
        }

        internal void WriteManifest(string revision, byte[] bytes) {
            if (!this.IsEnabled || !Revision.IsMatch(revision ?? string.Empty)) return;
            try {
                Directory.CreateDirectory(this._manifests);
                string path = Path.Combine(this._manifests, ManifestFileName(revision));
                if (!File.Exists(path)) WriteAtomic(path, bytes);
            }
            catch {
                // Cache failures do not invalidate a verified remote manifest.
            }
        }

        internal void QuarantineLatest() {
            if (!this.IsEnabled) return;
            Quarantine(Path.Combine(this._root, "latest.json"));
            Quarantine(Path.Combine(this._root, "latest.etag"));
        }

        internal void QuarantineManifest(string revision) {
            if (this.IsEnabled && Revision.IsMatch(revision ?? string.Empty)) {
                Quarantine(Path.Combine(this._manifests, ManifestFileName(revision)));
            }
        }

        private static void WriteAtomic(string path, byte[] bytes) {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static string ManifestFileName(string revision) {
            return revision.Substring("sha256:".Length) + ".json";
        }

        private static void Quarantine(string path) {
            try {
                if (File.Exists(path)) File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            }
            catch {
                // Best effort only.
            }
        }
    }
}
