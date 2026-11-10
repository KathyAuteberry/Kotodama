using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using EchoXIV.Plamo;
using EchoXIV.Services;
using Xunit;

namespace Kotodama.Tests;

public sealed class PlamoClientTests : IDisposable
{
    private readonly FakeHandler _handler = new();
    private readonly PlamoClient _client;

    public PlamoClientTests() => _client = new PlamoClient(_handler);

    public void Dispose() => _client.Dispose();

    private static PlamoSettings Settings(string url = "http://fenchurch.lan:11434", int timeoutSeconds = 8, string keepAlive = "30m") =>
        new(url, PlamoSettings.DefaultModel, keepAlive, 0f, 192, TimeSpan.FromSeconds(timeoutSeconds));

    private PlamoTranslatorService Service(PlamoSettings? settings = null, Func<TranslationDirection, string, PlamoPromptExtras>? extras = null) =>
        new(_client, () => settings ?? Settings(), extras);

    [Fact]
    public void Generate_body_matches_the_benchmark_request()
    {
        var body = JsonNode.Parse(PlamoClient.BuildGenerateBody(Settings(), "PROMPT"))!;

        Assert.Equal(PlamoSettings.DefaultModel, body["model"]!.GetValue<string>());
        Assert.Equal("PROMPT", body["prompt"]!.GetValue<string>());
        Assert.True(body["raw"]!.GetValue<bool>());
        Assert.False(body["stream"]!.GetValue<bool>());
        Assert.Equal("30m", body["keep_alive"]!.GetValue<string>());
        Assert.Equal(0f, body["options"]!["temperature"]!.GetValue<float>());
        Assert.Equal(192, body["options"]!["num_predict"]!.GetValue<int>());
        Assert.Equal(new[] { "<|plamo:tag|>", "<|plamo:bos|>" }, body["options"]!["stop"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void Empty_keep_alive_is_omitted_and_num_predict_has_a_default()
    {
        var body = JsonNode.Parse(PlamoClient.BuildGenerateBody(Settings(keepAlive: "") with { NumPredict = 0 }, "p"))!;

        Assert.Null(body["keep_alive"]);
        Assert.Equal(PlamoSettings.DefaultNumPredict, body["options"]!["num_predict"]!.GetValue<int>());
    }

    [Fact]
    public void Japanese_text_is_not_escaped_in_the_body()
    {
        Assert.Contains("軽減", PlamoClient.BuildGenerateBody(Settings(), "軽減"));
    }

    [Theory]
    [InlineData("http://fenchurch.lan:11434", "http://fenchurch.lan:11434/api/generate")]
    [InlineData("http://fenchurch.lan:11434/", "http://fenchurch.lan:11434/api/generate")]
    [InlineData("fenchurch.lan:11434", "http://fenchurch.lan:11434/api/generate")]
    [InlineData("http://10.1.0.103:11434/api", "http://10.1.0.103:11434/api/generate")]
    [InlineData("http://10.1.0.103:11434/v1/", "http://10.1.0.103:11434/api/generate")]
    [InlineData("https://ollama.example.com", "https://ollama.example.com/api/generate")]
    public void Base_urls_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, PlamoClient.BuildUrl(input, "/api/generate").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://host")]
    [InlineData("http://")]
    public void Unusable_urls_are_rejected(string input)
    {
        Assert.Throws<PlamoUnavailableException>(() => PlamoClient.BuildUrl(input, "/api/generate"));
    }

    [Fact]
    public async Task Translates_japanese_to_english_with_an_explicit_direction()
    {
        _handler.Respond("""{"response":"Please use mitigation.","eval_count":5,"total_duration":160000000}""");

        var result = await Service().TranslateAsync("軽減お願いします", "auto", "en");

        Assert.Equal("Please use mitigation.", result);
        var request = _handler.Requests.Single();
        Assert.Equal("http://fenchurch.lan:11434/api/generate", request.Url);
        var prompt = JsonNode.Parse(request.Body)!["prompt"]!.GetValue<string>();
        Assert.EndsWith("<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>en<|plamo:msg|>", prompt);
    }

    [Fact]
    public async Task Translates_english_to_polite_japanese()
    {
        _handler.Respond("""{"response":"ご飯切れてないか確認お願いします。"}""");

        await Service().TranslateAsync("Please make sure your food hasn't expired.", "auto", "ja");

        var prompt = JsonNode.Parse(_handler.Requests.Single().Body)!["prompt"]!.GetValue<string>();
        Assert.EndsWith("<|plamo:val|>ja<|plamo:key|>form<|plamo:val|>polite<|plamo:msg|>", prompt);
    }

    [Theory]
    [InlineData("stack", "en")]
    [InlineData("散開", "ja")]
    [InlineData("o/", "en")]
    public async Task Text_already_in_the_target_language_is_returned_without_a_request(string text, string target)
    {
        Assert.Equal(text, await Service().TranslateAsync(text, "auto", target));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Unsupported_target_language_is_rejected()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Service().TranslateAsync("hello", "auto", "es"));
    }

    [Fact]
    public async Task Output_is_cleaned_to_the_first_translation()
    {
        _handler.Respond("""{"response":"Please use mitigation.\nAlternatively: mitigate.<|plamo:tag|>"}""");

        Assert.Equal("Please use mitigation.", await Service().TranslateAsync("軽減お願いします", "auto", "en"));
    }

    [Fact]
    public async Task Empty_output_is_a_failure()
    {
        _handler.Respond("""{"response":"   "}""");

        await Assert.ThrowsAsync<PlamoUnavailableException>(() => Service().TranslateAsync("軽減お願いします", "auto", "en"));
    }

    [Fact]
    public async Task Http_error_is_unavailable()
    {
        _handler.Respond("""{"error":"model not found"}""", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<PlamoUnavailableException>(() => Service().TranslateAsync("軽減お願いします", "auto", "en"));
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task Invalid_json_is_unavailable()
    {
        _handler.Respond("<html>proxy error</html>");

        await Assert.ThrowsAsync<PlamoUnavailableException>(() => Service().TranslateAsync("軽減お願いします", "auto", "en"));
    }

    [Fact]
    public async Task Connection_failure_is_unavailable_after_one_retry()
    {
        _handler.Fail = () => new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        await Assert.ThrowsAsync<PlamoUnavailableException>(() => Service().TranslateAsync("軽減お願いします", "auto", "en"));
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Slow_server_hits_the_instance_timeout()
    {
        _handler.Delay = TimeSpan.FromSeconds(10);

        await Assert.ThrowsAsync<PlamoTimeoutException>(() => Service(Settings(timeoutSeconds: 1) with { Timeout = TimeSpan.FromMilliseconds(100) })
            .TranslateAsync("軽減お願いします", "auto", "en"));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        _handler.Delay = TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().TranslateAsync("軽減お願いします", "auto", "en", cts.Token));
        Assert.IsNotType<PlamoTimeoutException>(ex);
    }

    [Fact]
    public async Task Profile_extras_are_added_to_the_prompt()
    {
        _handler.Respond("""{"response":"MT takes it with invuln."}""");
        var service = Service(extras: (direction, text) =>
            new PlamoPromptExtras("Raid chat.", new[] { new GlossaryTerm("無敵", "invuln") }, null));

        await service.TranslateAsync("MT無敵受けで", "auto", "en");

        var prompt = JsonNode.Parse(_handler.Requests.Single().Body)!["prompt"]!.GetValue<string>();
        Assert.Contains("<|plamo:tag|>context<|plamo:msg|>Raid chat.", prompt);
        Assert.Contains("<|plamo:tag|>glossary<|plamo:index|>無敵<|plamo:item|>invuln", prompt);
    }

    [Fact]
    public async Task Version_and_model_info_are_parsed()
    {
        _handler.Respond("""{"version":"0.40.15"}""");
        Assert.Equal("0.40.15", await _client.GetVersionAsync("http://h:11434", default));

        _handler.Respond("""{"details":{"family":"plamo3","parameter_size":"8.53B","quantization_level":"Q4_K_M"}}""");
        var info = await _client.ShowModelAsync("http://h:11434", PlamoSettings.DefaultModel, default);
        Assert.Equal("plamo3", info.Family);
        Assert.Equal("Q4_K_M", info.Quantization);
    }

    private sealed record Captured(string Url, string Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(string Body, HttpStatusCode Status)> _responses = new();
        public List<Captured> Requests { get; } = new();
        public TimeSpan Delay { get; set; }
        public Func<Exception>? Fail { get; set; }

        public void Respond(string body, HttpStatusCode status = HttpStatusCode.OK) => _responses.Enqueue((body, status));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Captured(request.RequestUri!.ToString(), body));

            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            if (Fail != null)
                throw Fail();

            var (responseBody, status) = _responses.Count > 0 ? _responses.Dequeue() : ("{}", HttpStatusCode.OK);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
