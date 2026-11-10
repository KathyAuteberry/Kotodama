using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EchoXIV.Plamo;

namespace EchoXIV.Services
{
    /// <summary>Profile data added to a PLaMo request (P4): context, matching glossary terms and register.</summary>
    public sealed record PlamoPromptExtras(string? Context, IReadOnlyList<GlossaryTerm> Glossary, string? Form)
    {
        public static readonly PlamoPromptExtras None = new(null, Array.Empty<GlossaryTerm>(), null);
    }

    /// <summary>
    /// Dedicated PLaMo 3 Translate engine (docs/PLAN.md P3.3): explicit JA⇄EN direction, PFML3
    /// prompt over raw <c>/api/generate</c>, per-instance timeout and cleanup to the first
    /// translation. Dalamud-free. Does not implement romanization.
    /// </summary>
    public sealed class PlamoTranslatorService : ITranslationService
    {
        private readonly PlamoClient _client;
        private readonly Func<PlamoSettings> _settings;
        private readonly Func<TranslationDirection, string, PlamoPromptExtras>? _extras;

        public PlamoTranslatorService(
            PlamoClient client,
            Func<PlamoSettings> settings,
            Func<TranslationDirection, string, PlamoPromptExtras>? extras = null)
        {
            _client = client;
            _settings = settings;
            _extras = extras;
        }

        public string Name => "PLaMo";

        public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var direction = DirectionResolver.ForTargetLanguage(targetLang, text, out var supported);
            if (!supported)
                throw new NotSupportedException($"PLaMo only translates between Japanese and English (requested '{targetLang}').");

            // Already in the target language, or no letters (o/, ；；): nothing to translate.
            if (direction == null)
                return text;

            var extras = _extras?.Invoke(direction.Value, text) ?? PlamoPromptExtras.None;
            var prompt = Pfml3PromptBuilder.Build(new Pfml3Request(direction.Value, text)
            {
                Context = extras.Context,
                Glossary = extras.Glossary,
                Form = extras.Form,
            });

            var settings = _settings();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.Timeout);

            PlamoGenerateResult result;
            try
            {
                result = await _client.GenerateAsync(settings, prompt, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PlamoTimeoutException(settings.Timeout);
            }

            var cleaned = PlamoOutputCleaner.Clean(result.Response, text);
            if (cleaned.Length == 0)
                throw new PlamoUnavailableException("PLaMo returned an empty translation.");

            return cleaned;
        }

        public void Dispose()
        {
            // The PlamoClient is shared and owned by the plugin.
        }
    }
}
