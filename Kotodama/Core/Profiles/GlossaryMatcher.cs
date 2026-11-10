using System;
using System.Collections.Generic;
using System.Linq;
using EchoXIV.Plamo;

namespace EchoXIV.Profiles
{
    /// <summary>
    /// Picks the profile glossary entries that occur in a message (docs/PLAN.md P4.3), so PLaMo only
    /// receives relevant terms: shorter prompts are faster and less likely to leak unrelated terms.
    /// </summary>
    public static class GlossaryMatcher
    {
        public const int DefaultCap = 24;

        private static readonly string[] EnglishSuffixes = { "", "s", "es", "d", "ed", "ing" };
        private static readonly string[] DroppedESuffixes = { "ing", "ed" };

        private sealed record Span(int Start, int Length, string Source, string Target);

        /// <summary>
        /// Returns matching terms in source order, longest match first without overlaps
        /// (反時計回り wins over the 時計回り inside it; "pair stack" over "stack").
        /// JA→EN: substring match on the Japanese term (ASCII terms such as D1 need word boundaries).
        /// EN→JA: case-insensitive match on every English alias with ASCII word boundaries and simple
        /// inflections (stack, stacks, stacking; not stackable). The source term sent to the model is
        /// the text as it appears in the message.
        /// </summary>
        public static IReadOnlyList<GlossaryTerm> Match(
            string text,
            TranslationDirection direction,
            IEnumerable<ProfileGlossaryEntry> entries,
            int cap = DefaultCap)
        {
            if (string.IsNullOrEmpty(text) || cap <= 0)
                return Array.Empty<GlossaryTerm>();

            var candidates = new List<Span>();
            foreach (var entry in entries)
            {
                if (!entry.AppliesTo(direction))
                    continue;

                if (direction == TranslationDirection.JaToEn)
                {
                    foreach (var start in FindAll(text, entry.Ja, StringComparison.Ordinal))
                    {
                        if (IsAsciiWord(entry.Ja) && !HasAsciiBoundaries(text, start, entry.Ja.Length))
                            continue;
                        candidates.Add(new Span(start, entry.Ja.Length, entry.Ja, entry.CanonicalEn));
                    }
                }
                else
                {
                    foreach (var alias in entry.En)
                    {
                        AddEnglish(candidates, text, alias, EnglishSuffixes, entry.Ja);

                        // "raise" → "raising"/"raised": the stem without its final e.
                        if (alias.Length > 2 && alias.EndsWith("e", StringComparison.OrdinalIgnoreCase))
                            AddEnglish(candidates, text, alias[..^1], DroppedESuffixes, entry.Ja);
                    }
                }
            }

            var accepted = new List<Span>();
            foreach (var span in candidates.OrderByDescending(s => s.Length).ThenBy(s => s.Start))
            {
                if (accepted.All(a => span.Start >= a.Start + a.Length || a.Start >= span.Start + span.Length))
                    accepted.Add(span);
            }

            var result = new List<GlossaryTerm>();
            var seen = new HashSet<(string, string)>();
            foreach (var span in accepted.OrderBy(s => s.Start))
            {
                if (!seen.Add((span.Source.ToLowerInvariant(), span.Target)))
                    continue;
                result.Add(new GlossaryTerm(span.Source, span.Target));
                if (result.Count >= cap)
                    break;
            }

            return result;
        }

        private static void AddEnglish(List<Span> candidates, string text, string stem, string[] suffixes, string target)
        {
            foreach (var start in FindAll(text, stem, StringComparison.OrdinalIgnoreCase))
            {
                if (start > 0 && IsAsciiAlnum(text[start - 1]))
                    continue;

                foreach (var suffix in suffixes)
                {
                    var suffixLength = TryMatchSuffix(text, start + stem.Length, suffix);
                    if (suffixLength < 0)
                        continue;

                    var length = stem.Length + suffixLength;
                    candidates.Add(new Span(start, length, text.Substring(start, length), target));
                    break;
                }
            }
        }

        private static int TryMatchSuffix(string text, int position, string suffix)
        {
            if (position + suffix.Length > text.Length)
                return -1;
            if (string.Compare(text, position, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return -1;
            var after = position + suffix.Length;
            return after < text.Length && IsAsciiAlnum(text[after]) ? -1 : suffix.Length;
        }

        private static IEnumerable<int> FindAll(string text, string term, StringComparison comparison)
        {
            if (term.Length == 0)
                yield break;

            var index = 0;
            while ((index = text.IndexOf(term, index, comparison)) >= 0)
            {
                yield return index;
                index++;
            }
        }

        private static bool HasAsciiBoundaries(string text, int start, int length) =>
            (start == 0 || !IsAsciiAlnum(text[start - 1])) &&
            (start + length >= text.Length || !IsAsciiAlnum(text[start + length]));

        private static bool IsAsciiWord(string term) => term.All(c => c < 128);

        private static bool IsAsciiAlnum(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    }
}
