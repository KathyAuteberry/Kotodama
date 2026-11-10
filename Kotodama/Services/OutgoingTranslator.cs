using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using EchoXIV.Outgoing;

namespace EchoXIV.Services
{
    /// <summary>
    /// Translates one queued outgoing line: cache lookup, glossary protection, the policy-filtered
    /// router, optional romanization and cache write. Throws on failure; the queue applies the
    /// failure policy. Runs on the queue's worker, never on the framework thread.
    /// </summary>
    internal sealed class OutgoingTranslator : IOutgoingTranslator
    {
        private readonly Func<ITranslationService> _currentTranslator;
        private readonly GlossaryService _glossary;
        private readonly TranslationCache _cache;
        private readonly Configuration _config;
        private readonly IPluginLog _log;
        private readonly Action _onAllEnginesFailed;

        public OutgoingTranslator(
            Func<ITranslationService> currentTranslator,
            GlossaryService glossary,
            TranslationCache cache,
            Configuration config,
            IPluginLog log,
            Action onAllEnginesFailed)
        {
            _currentTranslator = currentTranslator;
            _glossary = glossary;
            _cache = cache;
            _config = config;
            _log = log;
            _onAllEnginesFailed = onAllEnginesFailed;
        }

        private bool WantsRomanization =>
            _config.EnableRomanization && _config.RomanizationLanguages.Contains(_config.TargetLanguage);

        public async Task<OutgoingTranslation> TranslateAsync(OutgoingItem item, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var translator = _currentTranslator();
            var router = translator as TranslationRouter;
            var source = item.Text;
            var sourceLang = _config.SourceLanguage;
            var targetLang = _config.TargetLanguage;

            if (router != null &&
                _cache.TryGetWithRomanization(router.PrimaryCacheVariant, source, sourceLang, targetLang, out var cachedNative, out var cachedRomaji) &&
                // An untranslated line cached with glossary capitalisation (before 1.0.2.0) is ignored.
                GlossaryRoundTrip.FromCache(source, cachedNative!, text => _glossary.Protect(text).Text) != source)
            {
                var cachedToSend = WantsRomanization && !string.IsNullOrEmpty(cachedRomaji) ? cachedRomaji! : cachedNative!;
                Log("cache", stopwatch, source, cachedNative!, cachedToSend != cachedNative ? cachedToSend : null);
                return new OutgoingTranslation(cachedToSend, cachedNative!, cachedToSend != cachedNative ? cachedToSend : null);
            }

            var protection = _glossary.Protect(source);
            string raw;
            string? romanizedFromTranslation = null;
            var combined = false;
            string? cacheVariant = null;

            try
            {
                if (WantsRomanization && translator is ICombinedTranslator combinedTranslator)
                {
                    var result = await combinedTranslator.TranslateWithRomanizationAsync(
                        protection.Text, "auto", targetLang, cancellationToken, romanizeTranslation: true);
                    raw = result.Translation;
                    romanizedFromTranslation = result.RomanizedText;
                    combined = true;
                    if (router != null)
                        cacheVariant = router.CacheVariantFor(router.LastUsedInstanceId);
                }
                else if (router != null)
                {
                    var routed = await router.TranslateRoutedAsync(protection.Text, "auto", targetLang, cancellationToken);
                    raw = routed.Text;
                    cacheVariant = router.CacheVariantFor(routed.InstanceId);
                }
                else
                {
                    raw = await translator.TranslateAsync(protection.Text, "auto", targetLang, cancellationToken);
                }
            }
            catch (AllEnginesFailedException)
            {
                _onAllEnginesFailed();
                throw;
            }

            var native = GlossaryRoundTrip.Finish(source, protection.Text, raw, protection.Restore);
            var toSend = native;

            if (combined)
            {
                if (!string.IsNullOrWhiteSpace(romanizedFromTranslation))
                    toSend = romanizedFromTranslation!;
            }
            else if (WantsRomanization &&
                     NonLatinScriptDetector.Detect(native) != NonLatinScriptKind.Latin &&
                     RomanizerFor(translator) is { } romanizer)
            {
                try
                {
                    var romanized = await romanizer.RomanizeAsync(native, targetLang, cancellationToken);
                    if (!string.IsNullOrEmpty(romanized))
                        toSend = romanized!;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Warning(ex, "Romanization failed for outgoing message");
                }
            }

            var romanizedForCache = toSend != native ? toSend : null;
            if (cacheVariant != null && !string.IsNullOrEmpty(native) && native != source)
                _cache.Add(cacheVariant, source, sourceLang, targetLang, native, romanizedForCache);

            Log(router?.LastUsedInstanceName ?? translator.Name, stopwatch, source, native, romanizedForCache);
            return new OutgoingTranslation(toSend, native, romanizedForCache);
        }

        private static IRomanizationService? RomanizerFor(ITranslationService translator) =>
            translator is TranslationRouter router
                ? router.GetLastUsedTranslator() as IRomanizationService
                : translator as IRomanizationService;

        private void Log(string engine, Stopwatch stopwatch, string source, string native, string? romanized)
        {
            if (!_config.VerboseLogging)
                return;

            var romanization = string.IsNullOrWhiteSpace(romanized) ? string.Empty : $" (romaji: '{romanized}')";
            _log.Info($"Outgoing translation using {engine} to {_config.TargetLanguage} ({stopwatch.ElapsedMilliseconds}ms): '{source}' -> '{native}'{romanization}");
        }
    }
}
