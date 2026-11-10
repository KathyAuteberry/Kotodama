using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EchoXIV.Plamo;

namespace EchoXIV.Services
{
    public sealed record PlamoTestStep(string Name, bool Ok, string Detail, TimeSpan Elapsed);

    public sealed record PlamoTestResult(IReadOnlyList<PlamoTestStep> Steps)
    {
        public bool Ok => Steps.Count > 0 && Steps.All(s => s.Ok);
    }

    /// <summary>
    /// "Test connection" for a PLaMo instance (docs/PLAN.md P3.4): server reachable, model present
    /// and from the plamo3 family, then one sample translation per direction with its latency.
    /// Dalamud-free; the settings window runs it off the framework thread.
    /// </summary>
    public static class PlamoConnectionTest
    {
        public const string SampleJapanese = "軽減お願いします";
        public const string SampleEnglish = "Please wait, my game crashed.";

        public static async Task<PlamoTestResult> RunAsync(PlamoClient client, PlamoSettings settings, CancellationToken cancellationToken)
        {
            var steps = new List<PlamoTestStep>();

            var server = await StepAsync("Server", async () =>
                $"Ollama {await client.GetVersionAsync(settings.BaseUrl, cancellationToken).ConfigureAwait(false)}").ConfigureAwait(false);
            steps.Add(server);
            if (!server.Ok)
                return new PlamoTestResult(steps);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var info = await client.ShowModelAsync(settings.BaseUrl, settings.Model, cancellationToken).ConfigureAwait(false);
                var isPlamo = string.Equals(info.Family, "plamo3", StringComparison.OrdinalIgnoreCase);
                var detail = $"{settings.Model} (family {info.Family ?? "?"}, {info.ParameterSize ?? "?"}, {info.Quantization ?? "?"})";
                steps.Add(new PlamoTestStep("Model", isPlamo, isPlamo ? detail : detail + ": not a PLaMo 3 model", stopwatch.Elapsed));
                if (!isPlamo)
                    return new PlamoTestResult(steps);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                steps.Add(new PlamoTestStep("Model", false, ex.Message, stopwatch.Elapsed));
                return new PlamoTestResult(steps);
            }

            // The first request may load the model (about 3 s cold); allow for that beyond the
            // instance's normal timeout.
            var service = new PlamoTranslatorService(client, () => settings with { Timeout = settings.Timeout + TimeSpan.FromSeconds(20) });
            steps.Add(await StepAsync("JA→EN", async () =>
                $"{SampleJapanese} → {await service.TranslateAsync(SampleJapanese, "ja", "en", cancellationToken).ConfigureAwait(false)}").ConfigureAwait(false));
            steps.Add(await StepAsync("EN→JA", async () =>
                $"{SampleEnglish} → {await service.TranslateAsync(SampleEnglish, "en", "ja", cancellationToken).ConfigureAwait(false)}").ConfigureAwait(false));

            return new PlamoTestResult(steps);

            async Task<PlamoTestStep> StepAsync(string name, Func<Task<string>> action)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    var detail = await action().ConfigureAwait(false);
                    return new PlamoTestStep(name, true, detail, watch.Elapsed);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new PlamoTestStep(name, false, ex.Message, watch.Elapsed);
                }
            }
        }

        /// <summary>
        /// Loads the model so the first real translation is fast. Failures are ignored: this is only
        /// an optimization, and translation reports its own errors.
        /// </summary>
        public static async Task WarmUpAsync(PlamoClient client, PlamoSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                var prompt = Pfml3PromptBuilder.Build(new Pfml3Request(TranslationDirection.JaToEn, "了解"));
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await client.GenerateAsync(settings with { NumPredict = 8 }, prompt, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}
