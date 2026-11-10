using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EchoXIV.Plamo
{
    /// <summary>Per-request settings for one PLaMo instance (taken from its EngineInstance).</summary>
    public sealed record PlamoSettings(
        string BaseUrl,
        string Model,
        string KeepAlive,
        float Temperature,
        int NumPredict,
        TimeSpan Timeout)
    {
        public const string DefaultModel = "hf.co/pfnet/plamo-3-translate-8b-GGUF:Q4_K_M";
        public const string DefaultKeepAlive = "30m";
        public const int DefaultNumPredict = 192;
        public const int DefaultTimeoutSeconds = 8;
    }

    public sealed record PlamoGenerateResult(string Response, int EvalCount, TimeSpan TotalDuration, TimeSpan LoadDuration);

    public sealed record PlamoModelInfo(string Model, string? Family, string? ParameterSize, string? Quantization);

    /// <summary>The server is unreachable, returned an error, or the URL is invalid.</summary>
    public class PlamoUnavailableException : Exception
    {
        public PlamoUnavailableException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>The request exceeded the instance's timeout (not caused by the caller's cancellation).</summary>
    public sealed class PlamoTimeoutException : Exception
    {
        public PlamoTimeoutException(TimeSpan timeout) : base($"PLaMo did not answer within {timeout.TotalSeconds:0.#} s.")
        {
        }
    }

    /// <summary>
    /// Minimal Ollama client for PLaMo: raw <c>/api/generate</c> with a hand-built PFML3 prompt
    /// (chosen over the chat APIs after measuring, docs/AUDIT.md §4.2), plus <c>/api/version</c>
    /// and <c>/api/show</c> for the connection test. Owns its HttpClient; dispose on plugin unload.
    /// </summary>
    public sealed class PlamoClient : IDisposable
    {
        private readonly HttpClient _http;

        public PlamoClient(HttpMessageHandler? handler = null)
        {
            _http = handler == null
                ? new HttpClient(new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    ConnectTimeout = TimeSpan.FromSeconds(3),
                })
                : new HttpClient(handler, disposeHandler: false);

            // Every call carries its own cancellation token/timeout.
            _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        }

        public async Task<PlamoGenerateResult> GenerateAsync(PlamoSettings settings, string prompt, CancellationToken cancellationToken)
        {
            var url = BuildUrl(settings.BaseUrl, "/api/generate");
            var body = BuildGenerateBody(settings, prompt);

            using var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") },
                cancellationToken).ConfigureAwait(false);

            var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var text = json["response"]?.GetValue<string>() ?? string.Empty;
            return new PlamoGenerateResult(
                text,
                json["eval_count"]?.GetValue<int>() ?? 0,
                Nanoseconds(json["total_duration"]),
                Nanoseconds(json["load_duration"]));
        }

        public async Task<string> GetVersionAsync(string baseUrl, CancellationToken cancellationToken)
        {
            var url = BuildUrl(baseUrl, "/api/version");
            using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
            var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            return json["version"]?.GetValue<string>() ?? "unknown";
        }

        public async Task<PlamoModelInfo> ShowModelAsync(string baseUrl, string model, CancellationToken cancellationToken)
        {
            var url = BuildUrl(baseUrl, "/api/show");
            var body = new JsonObject { ["model"] = model }.ToJsonString();
            using var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") },
                cancellationToken).ConfigureAwait(false);
            var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var details = json["details"];
            return new PlamoModelInfo(
                model,
                details?["family"]?.GetValue<string>(),
                details?["parameter_size"]?.GetValue<string>(),
                details?["quantization_level"]?.GetValue<string>());
        }

        /// <summary>The JSON body for <c>/api/generate</c>. Public for tests.</summary>
        public static string BuildGenerateBody(PlamoSettings settings, string prompt)
        {
            var stop = new JsonArray();
            foreach (var s in Pfml3PromptBuilder.StopSequences)
                stop.Add(s);

            var body = new JsonObject
            {
                ["model"] = settings.Model,
                ["prompt"] = prompt,
                ["raw"] = true,
                ["stream"] = false,
            };
            if (!string.IsNullOrWhiteSpace(settings.KeepAlive))
                body["keep_alive"] = settings.KeepAlive.Trim();
            body["options"] = new JsonObject
            {
                ["temperature"] = settings.Temperature,
                ["num_predict"] = settings.NumPredict > 0 ? settings.NumPredict : PlamoSettings.DefaultNumPredict,
                ["stop"] = stop,
            };
            return body.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        /// <summary>
        /// Normalizes a user-entered base URL ("fenchurch.lan:11434", ".../api", ".../v1/") and appends
        /// <paramref name="path"/>. Throws <see cref="PlamoUnavailableException"/> for unusable input.
        /// </summary>
        public static Uri BuildUrl(string baseUrl, string path)
        {
            var value = (baseUrl ?? string.Empty).Trim();
            if (value.Length == 0)
                throw new PlamoUnavailableException("No server URL is configured.");

            if (!value.Contains("://", StringComparison.Ordinal))
                value = "http://" + value;

            if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(baseUri.Host))
            {
                throw new PlamoUnavailableException($"'{baseUrl}' is not a valid http(s) server URL.");
            }

            // Accept URLs copied with an API suffix ("…/api", "…/v1/").
            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            foreach (var suffix in new[] { "/api", "/v1" })
            {
                if (basePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    basePath = basePath[..^suffix.Length];
            }

            return new Uri(baseUri.GetLeftPart(UriPartial.Authority) + basePath + path);
        }

        private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = createRequest();
                try
                {
                    var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return response;

                    var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    response.Dispose();
                    throw new PlamoUnavailableException($"Ollama returned HTTP {status}: {Truncate(error)}");
                }
                catch (HttpRequestException ex) when (attempt == 1 && IsConnectionRefused(ex) && !cancellationToken.IsCancellationRequested)
                {
                    // Ollama restarting: one quick retry, then give up.
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new PlamoUnavailableException($"Can't reach the PLaMo server: {ex.Message}", ex);
                }
            }
        }

        private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return JsonNode.Parse(text) ?? throw new PlamoUnavailableException("Ollama returned an empty response.");
            }
            catch (JsonException ex)
            {
                throw new PlamoUnavailableException($"Ollama returned invalid JSON: {Truncate(text)}", ex);
            }
        }

        private static bool IsConnectionRefused(HttpRequestException ex) =>
            ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
            || ex.HttpRequestError == HttpRequestError.ConnectionError;

        private static TimeSpan Nanoseconds(JsonNode? node) =>
            node == null ? TimeSpan.Zero : TimeSpan.FromTicks(node.GetValue<long>() / 100);

        private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";

        public void Dispose() => _http.Dispose();
    }
}
