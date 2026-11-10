using System.Net;
using System.Net.Http;
using System.Text;
using EchoXIV.Plamo;
using EchoXIV.Services;
using Xunit;

namespace Kotodama.Tests;

public sealed class PlamoConnectionTestTests : IDisposable
{
    private readonly ScriptedHandler _handler = new();
    private readonly PlamoClient _client;

    public PlamoConnectionTestTests() => _client = new PlamoClient(_handler);

    public void Dispose() => _client.Dispose();

    private static PlamoSettings Settings => new("http://h:11434", PlamoSettings.DefaultModel, "30m", 0f, 192, TimeSpan.FromSeconds(8));

    [Fact]
    public async Task Healthy_server_passes_every_step()
    {
        _handler.On("/api/version", """{"version":"0.40.15"}""");
        _handler.On("/api/show", """{"details":{"family":"plamo3","parameter_size":"8.53B","quantization_level":"Q4_K_M"}}""");
        _handler.On("/api/generate", """{"response":"translated"}""");

        var result = await PlamoConnectionTest.RunAsync(_client, Settings, default);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "Server", "Model", "JA→EN", "EN→JA" }, result.Steps.Select(s => s.Name));
        Assert.Contains("0.40.15", result.Steps[0].Detail);
    }

    [Fact]
    public async Task Unreachable_server_stops_after_the_first_step()
    {
        _handler.Status = HttpStatusCode.BadGateway;

        var result = await PlamoConnectionTest.RunAsync(_client, Settings, default);

        Assert.False(result.Ok);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task Non_plamo_model_is_reported()
    {
        _handler.On("/api/version", """{"version":"0.40.15"}""");
        _handler.On("/api/show", """{"details":{"family":"qwen3"}}""");

        var result = await PlamoConnectionTest.RunAsync(_client, Settings, default);

        Assert.False(result.Ok);
        Assert.Contains("not a PLaMo 3 model", result.Steps[^1].Detail);
        Assert.DoesNotContain(result.Steps, s => s.Name == "JA→EN");
    }

    [Fact]
    public async Task Warm_up_never_throws()
    {
        _handler.Status = HttpStatusCode.InternalServerError;
        await PlamoConnectionTest.WarmUpAsync(_client, Settings, default);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public void On(string path, string body) => _routes[path] = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = _routes.TryGetValue(request.RequestUri!.AbsolutePath, out var b) ? b : "{}";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
