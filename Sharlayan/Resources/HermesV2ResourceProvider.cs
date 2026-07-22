namespace Sharlayan.Resources {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;

    using Sharlayan.Models.Resources;

    internal sealed class HermesV2ResourceProvider {
        private const string EmbeddedName = "Sharlayan.Resources.HermesV2.embedded.json";
        private readonly SharlayanConfiguration _configuration;
        private readonly IHermesV2Transport _transport;
        private readonly HermesV2ResourceCache _cache;
        private readonly string _clientVersion;

        internal HermesV2ResourceProvider(SharlayanConfiguration configuration, IHermesV2Transport transport = null, string clientVersion = null) {
            this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this._transport = transport ?? new HermesV2HttpTransport();
            this._cache = new HermesV2ResourceCache(configuration.ResourceCacheDirectory);
            this._clientVersion = clientVersion ?? GetClientVersion();
        }

        internal async Task<IReadOnlyList<HermesManifestCandidate>> GetCandidatesAsync(CancellationToken cancellationToken) {
            List<HermesManifestCandidate> candidates = new List<HermesManifestCandidate>();
            if (this._configuration.HermesV2ManifestOverride != null) {
                byte[] bytes = this._configuration.HermesV2ManifestOverride;
                HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(bytes, null, this._clientVersion, allowCandidate: true);
                string revision = HermesV2ManifestParser.CalculateRevision(bytes);
                candidates.Add(CreateCandidate(manifest, bytes, ResourceSource.Local, revision, "Local live-smoke override."));
                return candidates;
            }

            string fallbackReason = string.Empty;
            string attemptedRevision = null;

            if (this._configuration.ResourceMode == ResourceMode.RemotePreferred) {
                try {
                    ValidateRemoteConfiguration();
                    HermesManifestCandidate remote = await this.TryRemoteAsync(cancellationToken).ConfigureAwait(false);
                    candidates.Add(remote);
                    attemptedRevision = remote.Info.ResourceRevision;
                }
                catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested)) {
                    fallbackReason = "Remote: " + exception.GetType().Name + ": " + exception.Message;
                }

                HermesManifestCandidate cached = this.TryCache(fallbackReason, attemptedRevision);
                if (cached != null) {
                    candidates.Add(cached);
                    attemptedRevision = cached.Info.ResourceRevision;
                }
            }

            HermesManifestCandidate embedded = this.ReadEmbedded(fallbackReason);
            if (!ContainsRevision(candidates, embedded.Info.ResourceRevision)) {
                candidates.Add(embedded);
            }
            if (candidates.Count == 0) candidates.Add(embedded);
            return candidates;
        }

        private static bool ContainsRevision(IEnumerable<HermesManifestCandidate> candidates, string revision) {
            foreach (HermesManifestCandidate candidate in candidates) {
                if (string.Equals(candidate.Info.ResourceRevision, revision, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private async Task<HermesManifestCandidate> TryRemoteAsync(CancellationToken cancellationToken) {
            this._cache.TryReadLatest(out byte[] cachedLatestBytes, out string cachedEtag);
            HermesHttpResponse latestResponse = await this._transport.GetAsync(
                this._configuration.HermesV2LatestUri,
                cachedEtag,
                HermesV2ManifestParser.MaximumLatestBytes,
                NormalizedTimeout(),
                cancellationToken).ConfigureAwait(false);

            byte[] latestBytes;
            string latestEtag;
            if (latestResponse.StatusCode == HttpStatusCode.NotModified) {
                latestBytes = cachedLatestBytes ?? throw new InvalidDataException("Hermes returned 304 without a cached latest pointer.");
                latestEtag = cachedEtag;
            }
            else {
                latestBytes = latestResponse.Bytes;
                latestEtag = latestResponse.ETag;
            }

            HermesLatestPointer latest = HermesV2ManifestParser.ParseLatest(latestBytes);
            if (this._cache.TryReadManifest(latest.ResourceRevision, out byte[] cachedManifest)) {
                try {
                    HermesV2Manifest cached = HermesV2ManifestParser.ParseManifest(cachedManifest, latest.ResourceRevision, this._clientVersion, allowCandidate: false);
                    EnsureLatestMatchesManifest(latest, cached);
                    this._cache.WriteLatest(latestBytes, latestEtag);
                    return CreateCandidate(cached, cachedManifest, ResourceSource.Cache, latest.ResourceRevision, "Remote latest selected an existing verified cache entry.");
                }
                catch {
                    this._cache.QuarantineManifest(latest.ResourceRevision);
                }
            }

            Uri manifestUri = new Uri(this._configuration.HermesV2LatestUri, latest.Manifest);
            if (manifestUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(manifestUri.Authority, this._configuration.HermesV2LatestUri.Authority, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException("Hermes manifest URI escaped the configured HTTPS origin.");
            }

            HermesHttpResponse manifestResponse = await this._transport.GetAsync(
                manifestUri,
                null,
                HermesV2ManifestParser.MaximumManifestBytes,
                NormalizedTimeout(),
                cancellationToken).ConfigureAwait(false);
            if (manifestResponse.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Hermes immutable manifest was not returned.");
            HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(manifestResponse.Bytes, latest.ResourceRevision, this._clientVersion, allowCandidate: false);
            EnsureLatestMatchesManifest(latest, manifest);
            this._cache.WriteManifest(latest.ResourceRevision, manifestResponse.Bytes);
            this._cache.WriteLatest(latestBytes, latestEtag);
            return CreateCandidate(manifest, manifestResponse.Bytes, ResourceSource.Remote, latest.ResourceRevision, string.Empty);
        }

        private HermesManifestCandidate TryCache(string fallbackReason, string skipRevision) {
            if (this._cache.TryReadLatest(out byte[] latestBytes, out _)) {
                try {
                    HermesLatestPointer latest = HermesV2ManifestParser.ParseLatest(latestBytes);
                    if (!string.Equals(latest.ResourceRevision, skipRevision, StringComparison.Ordinal)
                        && this._cache.TryReadManifest(latest.ResourceRevision, out byte[] manifestBytes)) {
                        HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(manifestBytes, latest.ResourceRevision, this._clientVersion, allowCandidate: false);
                        EnsureLatestMatchesManifest(latest, manifest);
                        return CreateCandidate(manifest, manifestBytes, ResourceSource.Cache, latest.ResourceRevision, fallbackReason);
                    }
                }
                catch {
                    this._cache.QuarantineLatest();
                }
            }

            foreach ((string revision, byte[] bytes) in this._cache.ReadManifestFallbacks()) {
                if (string.Equals(revision, skipRevision, StringComparison.Ordinal)) continue;
                try {
                    HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(bytes, revision, this._clientVersion, allowCandidate: false);
                    return CreateCandidate(manifest, bytes, ResourceSource.Cache, revision, fallbackReason);
                }
                catch {
                    this._cache.QuarantineManifest(revision);
                }
            }

            return null;
        }

        private HermesManifestCandidate ReadEmbedded(string fallbackReason) {
            using (Stream stream = typeof(HermesV2ResourceProvider).Assembly.GetManifestResourceStream(EmbeddedName)
                                   ?? throw new InvalidDataException("Embedded Hermes v2 manifest is missing."))
            using (MemoryStream output = new MemoryStream()) {
                stream.CopyTo(output);
                byte[] bytes = output.ToArray();
                HermesV2Manifest manifest = HermesV2ManifestParser.ParseManifest(bytes, null, this._clientVersion, allowCandidate: true);
                string revision = HermesV2ManifestParser.CalculateRevision(bytes);
                return CreateCandidate(manifest, bytes, ResourceSource.Embedded, revision, fallbackReason);
            }
        }

        private HermesManifestCandidate CreateCandidate(HermesV2Manifest manifest, byte[] bytes, ResourceSource source, string revision, string fallbackReason) {
            ResourceInfo info = new ResourceInfo(
                source,
                revision,
                manifest.Source.FcsCommit,
                manifest.Source.GeneratorCommit,
                manifest.SchemaVersion,
                manifest.Validation.Status,
                fallbackReason);
            return new HermesManifestCandidate(manifest, bytes, info);
        }

        private void ValidateRemoteConfiguration() {
            Uri uri = this._configuration.HermesV2LatestUri;
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) {
                throw new InvalidDataException("HermesV2LatestUri must be an absolute HTTPS URI without query or fragment.");
            }
        }

        private TimeSpan NormalizedTimeout() {
            TimeSpan timeout = this._configuration.ResourceRequestTimeout;
            if (timeout < TimeSpan.FromMilliseconds(500) || timeout > TimeSpan.FromSeconds(30)) {
                throw new InvalidDataException("ResourceRequestTimeout must be between 500 ms and 30 seconds.");
            }
            return timeout;
        }

        private static void EnsureLatestMatchesManifest(HermesLatestPointer latest, HermesV2Manifest manifest) {
            if (!string.Equals(latest.FcsCommit, manifest.Source.FcsCommit, StringComparison.Ordinal)) {
                throw new InvalidDataException("Hermes latest pointer and manifest disagree on the FCS commit.");
            }
        }

        internal static string GetClientVersion() {
            Assembly assembly = typeof(HermesV2ResourceProvider).Assembly;
            string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
            Version version = assembly.GetName().Version;
            return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }
}
