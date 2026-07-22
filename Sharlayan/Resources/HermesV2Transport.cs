namespace Sharlayan.Resources {
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class HermesHttpResponse {
        internal HermesHttpResponse(HttpStatusCode statusCode, byte[] bytes, string etag) {
            this.StatusCode = statusCode;
            this.Bytes = bytes;
            this.ETag = etag;
        }

        internal HttpStatusCode StatusCode { get; }
        internal byte[] Bytes { get; }
        internal string ETag { get; }
    }

    internal interface IHermesV2Transport {
        Task<HermesHttpResponse> GetAsync(Uri uri, string etag, int maximumBytes, TimeSpan timeout, CancellationToken cancellationToken);
    }

    internal sealed class HermesV2HttpTransport : IHermesV2Transport {
        private static readonly HttpClient Client = CreateClient();

        public async Task<HermesHttpResponse> GetAsync(Uri uri, string etag, int maximumBytes, TimeSpan timeout, CancellationToken cancellationToken) {
            using (CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                timeoutSource.CancelAfter(timeout);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri)) {
                    if (!string.IsNullOrWhiteSpace(etag)) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    using (HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false)) {
                        Uri finalUri = response.RequestMessage?.RequestUri;
                        if (finalUri == null
                            || finalUri.Scheme != Uri.UriSchemeHttps
                            || !string.Equals(finalUri.Authority, uri.Authority, StringComparison.OrdinalIgnoreCase)) {
                            throw new InvalidDataException("Hermes redirect escaped the configured HTTPS origin.");
                        }

                        if (response.StatusCode == HttpStatusCode.NotModified) {
                            return new HermesHttpResponse(response.StatusCode, Array.Empty<byte>(), response.Headers.ETag?.ToString());
                        }

                        if (response.StatusCode != HttpStatusCode.OK) {
                            throw new HttpRequestException("Hermes returned HTTP " + (int)response.StatusCode + ".");
                        }

                        string contentType = response.Content.Headers.ContentType?.MediaType;
                        if (!string.IsNullOrEmpty(contentType)
                            && !string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(contentType, "text/json", StringComparison.OrdinalIgnoreCase)) {
                            throw new InvalidDataException("Hermes response content type is not JSON.");
                        }

                        if (response.Content.Headers.ContentLength > maximumBytes) {
                            throw new InvalidDataException("Hermes response exceeds the size limit.");
                        }

                        using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (MemoryStream output = new MemoryStream()) {
                            byte[] buffer = new byte[4096];
                            while (true) {
                                int read = await stream.ReadAsync(buffer, 0, buffer.Length, timeoutSource.Token).ConfigureAwait(false);
                                if (read == 0) break;
                                if (output.Length + read > maximumBytes) throw new InvalidDataException("Hermes response exceeds the size limit.");
                                output.Write(buffer, 0, read);
                            }

                            return new HermesHttpResponse(response.StatusCode, output.ToArray(), response.Headers.ETag?.ToString());
                        }
                    }
                }
            }
        }

        private static HttpClient CreateClient() {
            HttpClientHandler handler = new HttpClientHandler {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
            };
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }
}
