using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EchoXIV.Plamo;

namespace EchoXIV.Profiles
{
    /// <summary>
    /// Keeps things that must never be translated intact (docs/PLAN.md P4.5). Names and emoticons are
    /// sent to PLaMo as identity glossary entries (its native mechanism; bracket placeholders get
    /// mangled), and macro tags such as &lt;se.1&gt; are checked after translation: a dropped tag fails
    /// the translation instead of sending a broken macro line.
    /// </summary>
    public static class TokenGuard
    {
        private static readonly Regex MacroTag = new(@"<(?:[a-zA-Z][a-zA-Z0-9]*(?:\.[0-9]+)?|[0-9])>", RegexOptions.Compiled);

        private static readonly string[] Emoticons =
        {
            ";w;", ";_;", "T_T", ";;", "；；", "^^", "^_^", "orz", "xD", ":)", ":(", ":D", "<3", "o/", "\\o/",
        };

        /// <summary>Macro and placeholder tags in the text, in order (&lt;se.1&gt;, &lt;t&gt;, &lt;me&gt;, &lt;1&gt;, …).</summary>
        public static IReadOnlyList<string> MacroTags(string text) =>
            MacroTag.Matches(text ?? string.Empty).Select(m => m.Value).ToList();

        /// <summary>The first macro tag of <paramref name="source"/> that is missing from <paramref name="output"/>, or null.</summary>
        public static string? FindDroppedTag(string source, string output)
        {
            var remaining = MacroTags(output).GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var tag in MacroTags(source))
            {
                if (!remaining.TryGetValue(tag, out var count) || count == 0)
                    return tag;
                remaining[tag] = count - 1;
            }

            return null;
        }

        /// <summary>
        /// Identity glossary terms (X → X) for everything in <paramref name="text"/> that must stay as
        /// written: the given names and words (whole words, case-insensitive, longest first so
        /// "Kath Auteberry" wins over "Kath"), emoticons and macro tags.
        /// </summary>
        public static IReadOnlyList<GlossaryTerm> IdentityTerms(string text, IEnumerable<string> keepAsWritten)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<GlossaryTerm>();

            var terms = new List<GlossaryTerm>();
            var covered = new List<(int Start, int End)>();

            foreach (var word in keepAsWritten
                         .Select(w => w?.Trim() ?? string.Empty)
                         .Where(w => w.Length > 1)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(w => w.Length))
            {
                var index = 0;
                while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    var end = index + word.Length;
                    var latin = word.All(c => c < 0x250);
                    if (IsWordBoundary(text, index - 1, latin) && IsWordBoundary(text, end, latin) &&
                        !covered.Any(c => index < c.End && c.Start < end))
                    {
                        var surface = text[index..end];
                        covered.Add((index, end));
                        if (!terms.Any(t => string.Equals(t.Source, surface, StringComparison.OrdinalIgnoreCase)))
                            terms.Add(new GlossaryTerm(surface, surface));
                    }

                    index = end;
                }
            }

            foreach (var emoticon in Emoticons)
            {
                if (text.Contains(emoticon, StringComparison.Ordinal) && !terms.Any(t => t.Source.Contains(emoticon, StringComparison.Ordinal)))
                    terms.Add(new GlossaryTerm(emoticon, emoticon));
            }

            foreach (var tag in MacroTags(text).Distinct(StringComparer.OrdinalIgnoreCase))
                terms.Add(new GlossaryTerm(tag, tag));

            return terms;
        }

        /// <summary>
        /// Latin-script words only need non-Latin neighbours, so "Kathさん" matches "Kath";
        /// other scripts have no spaces and match as substrings.
        /// </summary>
        private static bool IsWordBoundary(string text, int index, bool latinWord)
        {
            if (!latinWord || index < 0 || index >= text.Length)
                return true;

            var c = text[index];
            return !(char.IsLetterOrDigit(c) && c < 0x250);
        }
    }
}
