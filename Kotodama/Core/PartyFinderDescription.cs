using System.Collections.Generic;
using System.Linq;
using System.Text;
using EchoXIV.Plamo;

namespace EchoXIV
{
    public enum PartyFinderPieceKind
    {
        /// <summary>Plain text.</summary>
        Text,

        /// <summary>The icon the game draws before an auto-translate phrase.</summary>
        PhraseBegin,

        /// <summary>The icon the game draws after an auto-translate phrase.</summary>
        PhraseEnd,

        /// <summary>A whole auto-translate phrase (an unresolved auto-translate payload).</summary>
        Phrase,

        /// <summary>A line break.</summary>
        NewLine,
    }

    /// <summary>One decoded piece of a Party Finder description.</summary>
    public readonly record struct PartyFinderPiece(PartyFinderPieceKind Kind, string Text = "");

    /// <summary>
    /// Turns a Party Finder description into what Kotodama translates (JA→EN only). In listing
    /// descriptions auto-translate phrases arrive as already-resolved text between two icons
    /// (begin/end), not as payloads; that text is kept verbatim like in chat (docs/PLAN.md P4.6).
    /// Dalamud-free; the addon is read in GameFunctions/PartyFinderMenu.cs.
    /// </summary>
    public static class PartyFinderDescription
    {
        public static IncomingText Parse(IEnumerable<PartyFinderPiece> pieces)
        {
            var segments = new List<ChatSegment>();
            var phrase = (StringBuilder?)null;

            foreach (var piece in pieces)
            {
                switch (piece.Kind)
                {
                    case PartyFinderPieceKind.PhraseBegin:
                        phrase = new StringBuilder();
                        break;
                    case PartyFinderPieceKind.PhraseEnd:
                        if (phrase != null)
                            segments.Add(new ChatSegment(true, phrase.ToString()));
                        phrase = null;
                        break;
                    case PartyFinderPieceKind.Phrase:
                        segments.Add(new ChatSegment(true, piece.Text));
                        break;
                    case PartyFinderPieceKind.NewLine:
                        AddText(segments, phrase, " ");
                        break;
                    default:
                        AddText(segments, phrase, piece.Text);
                        break;
                }
            }

            // A begin icon without an end: treat what followed as plain text.
            if (phrase is { Length: > 0 })
                segments.Add(new ChatSegment(false, phrase.ToString()));

            return AutoTranslateText.Split(segments);
        }

        /// <summary>True when the translatable text is Japanese, so PLaMo should translate it to English.</summary>
        public static bool IsJapanese(IncomingText text) =>
            ScriptLanguageClassifier.Classify(text.Translatable) == ScriptLanguage.Ja;

        /// <summary>The chat line for a translated listing, e.g. "[KT] PF (The Omega Protocol, Kath Auteberry): …".</summary>
        public static string FormatLine(string translation, string? duty, string? recruiter)
        {
            var source = string.Join(", ", new[] { duty, recruiter }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            return source.Length == 0
                ? $"{IncomingChatEcho.Prefix} PF: {translation.Trim()}"
                : $"{IncomingChatEcho.Prefix} PF ({source}): {translation.Trim()}";
        }

        private static void AddText(List<ChatSegment> segments, StringBuilder? phrase, string text)
        {
            if (phrase != null)
            {
                phrase.Append(text);
                return;
            }

            // Text that still carries the glyphs (already converted elsewhere) splits the same way.
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == AutoTranslateText.OpenGlyph)
                {
                    if (i > start) segments.Add(new ChatSegment(false, text[start..i]));
                    var end = text.IndexOf(AutoTranslateText.CloseGlyph, i + 1);
                    if (end < 0)
                    {
                        start = i;
                        break;
                    }

                    segments.Add(new ChatSegment(true, text[(i + 1)..end]));
                    i = end;
                    start = end + 1;
                }
            }

            if (start < text.Length)
                segments.Add(new ChatSegment(false, text[start..]));
        }
    }
}
