using System;

namespace EchoXIV
{
    /// <summary>
    /// Classifies engine types as self-hosted ("local") or third-party ("cloud").
    /// Classification is by engine type only and never looks at the endpoint URL: the guard
    /// exists to stop Kotodama from silently switching to a service the user did not choose,
    /// not to second-guess an endpoint the user typed in themselves (docs/PLAN.md P2.1).
    /// </summary>
    public static class EngineLocality
    {
        public static bool IsLocal(TranslationEngine engine) => engine switch
        {
            TranslationEngine.LocalAI => true,

            TranslationEngine.Google => false,
            TranslationEngine.Papago => false,
            TranslationEngine.Yandex => false,
            // Usually the public libretranslate.com service.
            TranslationEngine.LibreTranslate => false,
            TranslationEngine.ChatGPT => false,
            TranslationEngine.Gemini => false,
            TranslationEngine.Claude => false,
            TranslationEngine.DeepSeek => false,
            TranslationEngine.OpenRouter => false,

            // A new engine type must be classified explicitly before it can be used.
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Unclassified translation engine."),
        };

        public static bool IsLocal(EngineInstance instance) => IsLocal(instance.Type);
    }
}
