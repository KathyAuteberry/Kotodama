namespace EchoXIV
{
    /// <summary>
    /// Translation cache keys (docs/PLAN.md P2.5). A key identifies the text, the language pair and
    /// the "variant" that produced the translation: engine type, model and prompt version (profile is
    /// added in P4). Without the variant, switching engines would keep serving the previous engine's
    /// output, e.g. Google translations after moving to a local-only setup.
    /// </summary>
    public static class CacheKey
    {
        /// <summary>Bump when prompts change in a way that should invalidate cached translations.</summary>
        public const int PromptVersion = 1;

        /// <summary>Key format version. Entries with any other prefix are ignored and dropped on load.</summary>
        public const string KeyPrefix = "v3|";

        public static string Variant(TranslationEngine engine, string? model) =>
            $"{engine}:{(model ?? string.Empty).Trim()}:p{PromptVersion}";

        /// <summary>
        /// Variant for engines that use a translation profile (PLaMo): editing or switching the
        /// profile changes the key, so stale translations are never served.
        /// </summary>
        public static string Variant(TranslationEngine engine, string? model, string profileName, string profileHash) =>
            $"{Variant(engine, model)}:{profileName}:{profileHash}";

        /// <summary>Length-prefixed so no combination of fields can collide with another.</summary>
        public static string Build(string variant, string text, string sourceLang, string targetLang) =>
            $"{KeyPrefix}{variant.Length}:{variant}|{text.Length}:{text}{sourceLang.Length}:{sourceLang}{targetLang.Length}:{targetLang}";

        public static bool IsCurrent(string key) => key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);
    }
}
