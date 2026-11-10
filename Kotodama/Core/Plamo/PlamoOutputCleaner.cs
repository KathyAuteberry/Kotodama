using System;

namespace EchoXIV.Plamo
{
    /// <summary>Keeps only the first translation from a PLaMo completion.</summary>
    public static class PlamoOutputCleaner
    {
        private static readonly (char Open, char Close)[] Quotes =
        {
            ('"', '"'), ('“', '”'), ('「', '」'), ('\'', '\''),
        };

        public static string Clean(string? output, string source)
        {
            if (string.IsNullOrEmpty(output))
                return string.Empty;

            var text = output;

            // Anything after a stray PFML token is another turn, never part of the translation.
            var token = text.IndexOf("<|plamo:", StringComparison.Ordinal);
            if (token >= 0)
                text = text[..token];

            text = text.Trim();

            // Chat lines are single-line: keep the first non-empty line of the output.
            if (!source.Contains('\n'))
            {
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        text = trimmed;
                        break;
                    }
                }
            }

            text = StripWrappingQuotes(text, source.Trim());
            return text.Trim();
        }

        private static string StripWrappingQuotes(string text, string source)
        {
            if (text.Length < 2)
                return text;

            foreach (var (open, close) in Quotes)
            {
                // Keep quotes the user wrote themselves.
                if (text[0] == open && text[^1] == close && !(source.Length > 0 && source[0] == open))
                    return text[1..^1].Trim();
            }

            return text;
        }
    }
}
