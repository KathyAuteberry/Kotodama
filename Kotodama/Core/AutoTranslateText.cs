using System.Collections.Generic;
using System.Linq;
using System.Text;
using EchoXIV.Plamo;

namespace EchoXIV
{
    /// <summary>One piece of an incoming chat line: plain text or an auto-translate phrase.</summary>
    public sealed record ChatSegment(bool IsAutoTranslate, string Text);

    /// <summary>
    /// What to send to the translation engine for an incoming line, and the auto-translate phrases to
    /// show verbatim. Auto-translate phrases are already displayed in the reader's client language,
    /// so they are never sent to the model (docs/PLAN.md P4.6).
    /// </summary>
    public sealed record IncomingText(string Translatable, IReadOnlyList<string> Phrases)
    {
        /// <summary>The line is only auto-translate phrases plus punctuation/emoticons: nothing to translate.</summary>
        public bool OnlyAutoTranslate => Phrases.Count > 0 && ScriptLanguageClassifier.Classify(Translatable) == ScriptLanguage.Unknown;
    }

    public static class AutoTranslateText
    {
        /// <summary>The private-use glyphs the game draws around auto-translate phrases.</summary>
        public const char OpenGlyph = '';
        public const char CloseGlyph = '';

        /// <summary>Phrase text without the glyphs and surrounding spaces.</summary>
        public static string CleanPhrase(string payloadText) =>
            (payloadText ?? string.Empty).Replace(OpenGlyph.ToString(), string.Empty).Replace(CloseGlyph.ToString(), string.Empty).Trim();

        public static IncomingText Split(IEnumerable<ChatSegment> segments)
        {
            var text = new StringBuilder();
            var phrases = new List<string>();
            foreach (var segment in segments)
            {
                if (segment.IsAutoTranslate)
                {
                    var phrase = CleanPhrase(segment.Text);
                    if (phrase.Length > 0)
                        phrases.Add(phrase);
                    // Keep words on both sides apart.
                    if (text.Length > 0 && text[^1] != ' ')
                        text.Append(' ');
                }
                else
                {
                    text.Append(segment.Text);
                }
            }

            return new IncomingText(CollapseSpaces(text.ToString()), phrases);
        }

        /// <summary>Phrases first, in their original order, each in 【】, then the translation.</summary>
        public static string Compose(IReadOnlyList<string> phrases, string translation)
        {
            var prefix = string.Join(" ", phrases.Select(p => $"【{p}】"));
            if (prefix.Length == 0)
                return translation;
            return string.IsNullOrWhiteSpace(translation) ? prefix : $"{prefix} {translation.Trim()}";
        }

        private static string CollapseSpaces(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text.Trim())
            {
                if (c == ' ' && builder.Length > 0 && builder[^1] == ' ')
                    continue;
                builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
