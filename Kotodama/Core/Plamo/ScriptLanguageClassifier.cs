using System.Text;

namespace EchoXIV.Plamo
{
    public enum ScriptLanguage
    {
        /// <summary>No letters at all (o/, ；；, …, digits, emoji): pass through untranslated.</summary>
        Unknown,
        Ja,
        En,
    }

    /// <summary>
    /// EN/JA-only classifier by script (docs/AUDIT.md finding 9). Kotodama only ever translates
    /// between Japanese and English, so counting Japanese letters against Latin letters is both
    /// simpler and more reliable than a many-language detector, which misreads kanji-only Japanese
    /// (散開, MT無敵) as Chinese.
    /// </summary>
    public static class ScriptLanguageClassifier
    {
        public static ScriptLanguage Classify(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return ScriptLanguage.Unknown;

            // Classify a normalized copy only: ＯＫ → OK, ｵｹ → オケ. The original is never changed.
            var normalized = text.Normalize(NormalizationForm.FormKC);

            var japanese = 0;
            var latin = 0;
            for (var i = 0; i < normalized.Length; i++)
            {
                var c = normalized[i];
                if (IsLatinLetter(c))
                {
                    // A single isolated letter is an emote or marker (o/, o7, \o/, waymark B), not a word.
                    if (!IsLoneLetter(normalized, i))
                        latin++;
                }
                else if (IsIdeograph(c))
                {
                    japanese++;
                }
                else if (IsKana(c) && !IsLoneLetter(normalized, i))
                {
                    japanese++;
                }
            }

            if (japanese == 0 && latin == 0)
                return ScriptLanguage.Unknown;

            return japanese * 2 >= latin ? ScriptLanguage.Ja : ScriptLanguage.En;
        }

        /// <summary>
        /// A single kana or Latin letter with non-letters on both sides does not count. Kaomoji such
        /// as ¯\_(ツ)_/¯ or ヽ(°〇°)ﾉ don't turn English lines into Japanese, and emotes such as o/
        /// or o7 aren't English words. Kanji are exempt: a single kanji (草) is a real word.
        /// </summary>
        private static bool IsLoneLetter(string text, int index)
        {
            var before = index > 0 && IsLetter(text[index - 1]);
            var after = index + 1 < text.Length && IsLetter(text[index + 1]);
            return !before && !after;
        }

        private static bool IsLetter(char c) => IsLatinLetter(c) || IsIdeograph(c) || IsKana(c);

        /// <summary>Hiragana and katakana letters; marks such as ー, ・ and ヽ are not letters.</summary>
        private static bool IsKana(char c) =>
            c is >= 'ぁ' and <= 'ゖ'   // hiragana
              or >= 'ァ' and <= 'ヺ'   // katakana (excludes ・ー ヽヾヿ)
              or >= 'ㇰ' and <= 'ㇿ';  // katakana phonetic extensions

        /// <summary>CJK ideographs plus 々. 〇 is treated as a symbol.</summary>
        private static bool IsIdeograph(char c) =>
            c is >= '一' and <= '鿿'   // CJK unified ideographs
              or >= '㐀' and <= '䶿'   // extension A
              or >= '豈' and <= '﫿'   // compatibility ideographs
              or '々';                     // 々

        /// <summary>A–Z, a–z and accented Latin letters (Latin-1, Extended-A/B, Extended Additional).</summary>
        private static bool IsLatinLetter(char c) =>
            c is >= 'A' and <= 'Z'
              or >= 'a' and <= 'z'
              or (>= 'À' and <= 'ÿ' and not '×' and not '÷')
              or >= 'Ā' and <= 'ɏ'
              or >= 'Ḁ' and <= 'ỿ';
    }
}
