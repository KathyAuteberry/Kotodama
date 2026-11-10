using System;

namespace EchoXIV
{
    /// <summary>
    /// Puts glossary terms back after translation. The glossary swaps terms for placeholders before
    /// the engine sees the text and restores them in their stored spelling (`tea` comes back as `TEA`).
    /// When the engine didn't translate (an English line for an English reader passes through
    /// PLaMo unchanged), the player's own line is kept, so a capitalisation change never shows up
    /// as a "translation".
    /// </summary>
    public static class GlossaryRoundTrip
    {
        public static string Finish(string source, string protectedText, string translation, Func<string, string> restore)
        {
            if (string.Equals(translation, protectedText, StringComparison.Ordinal))
                return source;

            return restore(translation);
        }

        /// <summary>
        /// A cached translation that matches the source once glossary terms are masked is such a
        /// pass-through (cached before this fix): serve the player's own line instead.
        /// </summary>
        public static string FromCache(string source, string cached, Func<string, string> protect)
        {
            if (string.Equals(protect(cached), protect(source), StringComparison.Ordinal))
                return source;

            return cached;
        }
    }
}
