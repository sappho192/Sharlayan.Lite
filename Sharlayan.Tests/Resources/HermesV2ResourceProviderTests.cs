namespace Sharlayan.Tests.Resources {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Newtonsoft.Json;

    using Sharlayan.Models.Resources;
    using Sharlayan.Resources;

    using Xunit;

    public sealed class HermesV2ResourceProviderTests {
        [Fact]
        public async Task RemoteManifestIsCachedAndUsedWhenNetworkFails() {
            string cacheDirectory = Path.Combine(Path.GetTempPath(), "sharlayan-hermes-tests-" + Guid.NewGuid().ToString("N"));
            try {
                byte[] manifestBytes = HermesV2ManifestTests.CreateLiveManifest();
                string revision = HermesV2ManifestParser.CalculateRevision(manifestBytes);
                Uri latestUri = new Uri("https://example.test/v2/latest.json");
                byte[] latestBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {
                    schemaVersion = 2,
                    resourceRevision = revision,
                    manifest = "manifests/" + revision + ".json",
                    fcsCommit = "8ff04195c4e77ef0b85d15c6fd1c67785378f0fb",
                    publishedAt = "2026-07-22T00:00:00Z",
                }));
                FakeTransport transport = new FakeTransport(
                    new HermesHttpResponse(HttpStatusCode.OK, latestBytes, "\"v1\""),
                    new HermesHttpResponse(HttpStatusCode.OK, manifestBytes, null));
                SharlayanConfiguration configuration = new SharlayanConfiguration {
                    ResourceMode = ResourceMode.RemotePreferred,
                    HermesV2LatestUri = latestUri,
                    ResourceCacheDirectory = cacheDirectory,
                };

                IReadOnlyList<HermesManifestCandidate> remote = await new HermesV2ResourceProvider(configuration, transport, "9.1.2").GetCandidatesAsync(CancellationToken.None);
                Assert.True(remote[0].Info.Source == ResourceSource.Remote, remote[0].Info.FallbackReason);
                HermesV2ResourceCache cache = new HermesV2ResourceCache(cacheDirectory);
                Assert.True(cache.TryReadLatest(out byte[] cachedLatest, out _), remote[0].Info.FallbackReason);
                HermesLatestPointer cachedPointer = HermesV2ManifestParser.ParseLatest(cachedLatest);
                Assert.True(cache.TryReadManifest(cachedPointer.ResourceRevision, out byte[] cachedBytes));
                Assert.NotNull(HermesV2ManifestParser.ParseManifest(cachedBytes, cachedPointer.ResourceRevision, "9.1.2", allowCandidate: false));
                IReadOnlyList<HermesManifestCandidate> fallback = await new HermesV2ResourceProvider(configuration, new ThrowingTransport(), "9.1.2").GetCandidatesAsync(CancellationToken.None);

                Assert.Equal(revision, remote[0].Info.ResourceRevision);
                Assert.Equal(ResourceSource.Cache, fallback[0].Info.Source);
                Assert.Equal(revision, fallback[0].Info.ResourceRevision);
                Assert.Contains("Remote:", fallback[0].Info.FallbackReason);
                Assert.Equal(ResourceSource.Embedded, fallback[1].Info.Source);
            }
            finally {
                if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task EmbeddedOnlyDoesNotCallNetwork() {
            SharlayanConfiguration configuration = new SharlayanConfiguration { ResourceMode = ResourceMode.EmbeddedOnly };
            CountingTransport transport = new CountingTransport();

            IReadOnlyList<HermesManifestCandidate> candidates = await new HermesV2ResourceProvider(configuration, transport, "9.1.2").GetCandidatesAsync(CancellationToken.None);

            Assert.Equal(0, transport.CallCount);
            Assert.Single(candidates);
            Assert.Equal(ResourceSource.Embedded, candidates[0].Info.Source);
        }

        [Fact]
        public async Task InvalidRemoteConfigurationFallsBackWithoutNetwork() {
            SharlayanConfiguration configuration = new SharlayanConfiguration {
                ResourceMode = ResourceMode.RemotePreferred,
                HermesV2LatestUri = new Uri("http://example.test/v2/latest.json"),
            };
            CountingTransport transport = new CountingTransport();

            IReadOnlyList<HermesManifestCandidate> candidates = await new HermesV2ResourceProvider(configuration, transport, "9.1.2").GetCandidatesAsync(CancellationToken.None);

            Assert.Equal(0, transport.CallCount);
            Assert.Single(candidates);
            Assert.Equal(ResourceSource.Embedded, candidates[0].Info.Source);
            Assert.Contains("Remote:", candidates[0].Info.FallbackReason);
        }

        [Fact]
        public async Task NotModifiedLatestUsesVerifiedCache() {
            string cacheDirectory = Path.Combine(Path.GetTempPath(), "sharlayan-hermes-tests-" + Guid.NewGuid().ToString("N"));
            try {
                byte[] manifestBytes = HermesV2ManifestTests.CreateLiveManifest();
                string revision = HermesV2ManifestParser.CalculateRevision(manifestBytes);
                byte[] latestBytes = CreateLatest(revision);
                HermesV2ResourceCache cache = new HermesV2ResourceCache(cacheDirectory);
                cache.WriteManifest(revision, manifestBytes);
                cache.WriteLatest(latestBytes, "\"v1\"");
                SharlayanConfiguration configuration = new SharlayanConfiguration {
                    ResourceMode = ResourceMode.RemotePreferred,
                    HermesV2LatestUri = new Uri("https://example.test/v2/latest.json"),
                    ResourceCacheDirectory = cacheDirectory,
                };

                IReadOnlyList<HermesManifestCandidate> candidates = await new HermesV2ResourceProvider(
                    configuration,
                    new FakeTransport(new HermesHttpResponse(HttpStatusCode.NotModified, Array.Empty<byte>(), "\"v1\"")),
                    "9.1.2").GetCandidatesAsync(CancellationToken.None);

                Assert.Equal(ResourceSource.Cache, candidates[0].Info.Source);
                Assert.Equal(revision, candidates[0].Info.ResourceRevision);
            }
            finally {
                if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task CorruptCacheIsQuarantinedBeforeEmbeddedFallback() {
            string cacheDirectory = Path.Combine(Path.GetTempPath(), "sharlayan-hermes-tests-" + Guid.NewGuid().ToString("N"));
            try {
                byte[] corrupt = Encoding.UTF8.GetBytes("{not-json}");
                string revision = HermesV2ManifestParser.CalculateRevision(corrupt);
                HermesV2ResourceCache cache = new HermesV2ResourceCache(cacheDirectory);
                cache.WriteManifest(revision, corrupt);
                cache.WriteLatest(CreateLatest(revision), null);
                SharlayanConfiguration configuration = new SharlayanConfiguration {
                    ResourceMode = ResourceMode.RemotePreferred,
                    HermesV2LatestUri = new Uri("https://example.test/v2/latest.json"),
                    ResourceCacheDirectory = cacheDirectory,
                };

                IReadOnlyList<HermesManifestCandidate> candidates = await new HermesV2ResourceProvider(configuration, new ThrowingTransport(), "9.1.2").GetCandidatesAsync(CancellationToken.None);

                Assert.Single(candidates);
                Assert.Equal(ResourceSource.Embedded, candidates[0].Info.Source);
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(cacheDirectory, "hermes-v2", "manifests"), "*.corrupt-*"));
            }
            finally {
                if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
            }
        }

        [Fact]
        public void CacheClearsStaleEtagWhenLatestResponseHasNoEtag() {
            string cacheDirectory = Path.Combine(Path.GetTempPath(), "sharlayan-hermes-tests-" + Guid.NewGuid().ToString("N"));
            try {
                HermesV2ResourceCache cache = new HermesV2ResourceCache(cacheDirectory);
                byte[] latest = CreateLatest("sha256:" + new string('a', 64));
                cache.WriteLatest(latest, "\"v1\"");
                cache.WriteLatest(latest, null);

                Assert.True(cache.TryReadLatest(out _, out string etag));
                Assert.Null(etag);
            }
            finally {
                if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
            }
        }

        private static byte[] CreateLatest(string revision) {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {
                schemaVersion = 2,
                resourceRevision = revision,
                manifest = "manifests/" + revision + ".json",
                fcsCommit = "8ff04195c4e77ef0b85d15c6fd1c67785378f0fb",
                publishedAt = "2026-07-22T00:00:00Z",
            }));
        }

        private sealed class FakeTransport : IHermesV2Transport {
            private readonly Queue<HermesHttpResponse> _responses;
            internal FakeTransport(params HermesHttpResponse[] responses) => this._responses = new Queue<HermesHttpResponse>(responses);
            public Task<HermesHttpResponse> GetAsync(Uri uri, string etag, int maximumBytes, TimeSpan timeout, CancellationToken cancellationToken) {
                return Task.FromResult(this._responses.Dequeue());
            }
        }

        private sealed class ThrowingTransport : IHermesV2Transport {
            public Task<HermesHttpResponse> GetAsync(Uri uri, string etag, int maximumBytes, TimeSpan timeout, CancellationToken cancellationToken) {
                throw new IOException("offline");
            }
        }

        private sealed class CountingTransport : IHermesV2Transport {
            internal int CallCount { get; private set; }
            public Task<HermesHttpResponse> GetAsync(Uri uri, string etag, int maximumBytes, TimeSpan timeout, CancellationToken cancellationToken) {
                this.CallCount++;
                throw new InvalidOperationException();
            }
        }
    }
}
