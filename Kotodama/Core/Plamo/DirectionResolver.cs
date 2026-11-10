namespace EchoXIV.Plamo
{
    /// <summary>Picks the PLaMo direction for a message, or decides it needs no translation.</summary>
    public static class DirectionResolver
    {
        /// <summary>Incoming chat for an English reader: translate Japanese lines only.</summary>
        public static TranslationDirection? ForIncoming(string text) =>
            ScriptLanguageClassifier.Classify(text) == ScriptLanguage.Ja ? TranslationDirection.JaToEn : null;

        /// <summary>
        /// Outgoing chat: the configured direction, unless detection is on, in which case the
        /// message's own script decides. Null means "nothing to translate" (no letters).
        /// </summary>
        public static TranslationDirection? ForOutgoing(TranslationDirection configured, bool detect, string text)
        {
            if (!detect)
                return configured;

            return ScriptLanguageClassifier.Classify(text) switch
            {
                ScriptLanguage.Ja => TranslationDirection.JaToEn,
                ScriptLanguage.En => TranslationDirection.EnToJa,
                _ => null,
            };
        }

        /// <summary>
        /// Maps a target language code (as passed through <c>ITranslationService</c>) to a direction,
        /// and returns null when the message is already in the target language or has no letters,
        /// meaning the text should be returned unchanged instead of sent to the model.
        /// Unsupported target languages also return null with <paramref name="supported"/> false.
        /// </summary>
        public static TranslationDirection? ForTargetLanguage(string targetLang, string text, out bool supported)
        {
            var target = (targetLang ?? string.Empty).Trim().ToLowerInvariant();
            supported = target is "ja" or "en";
            if (!supported)
                return null;

            var script = ScriptLanguageClassifier.Classify(text);
            return target switch
            {
                "en" when script == ScriptLanguage.Ja => TranslationDirection.JaToEn,
                "ja" when script == ScriptLanguage.En => TranslationDirection.EnToJa,
                _ => null,
            };
        }
    }
}
