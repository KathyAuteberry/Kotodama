using System.Text;

namespace EchoXIV.Outgoing
{
    /// <summary>Why an outgoing translation was not sent as translated.</summary>
    public enum OutgoingFailureKind
    {
        Timeout,
        EngineFailed,
        Empty,
        StartsWithSlash,
        TooLong,
        Unchanged,

        /// <summary>The user cancelled the translation preview. The original is never sent.</summary>
        Cancelled,

        /// <summary>The translation preview was not answered in time; treated as cancelled.</summary>
        ConfirmationTimeout,
    }

    /// <summary>What to do with a line whose translation failed (decided per item at failure time).</summary>
    public enum OutgoingFailurePolicy
    {
        /// <summary>Default: send nothing and print a local error that includes the original text.</summary>
        DoNotSend = 0,

        /// <summary>Send the original, untranslated text to the captured channel.</summary>
        SendOriginal = 1,
    }

    /// <summary>
    /// Checks a translation before it is handed to the game (docs/PLAN.md P1.3). Anything sent
    /// through the chat box that starts with '/' runs as a command, and over-long lines used to be
    /// silently truncated; both now fail the item instead.
    /// </summary>
    public static class OutgoingGuards
    {
        /// <summary>The game's chat input limit in UTF-8 bytes.</summary>
        public const int MaxChatBytes = 450;

        /// <summary>Removes control characters and folds line breaks into spaces.</summary>
        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c is '\r' or '\n' or '\t')
                    builder.Append(' ');
                else if (!char.IsControl(c))
                    builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Validates <paramref name="translated"/> and builds the line to send.
        /// Returns null on success (with <paramref name="line"/> set), otherwise the failure kind.
        /// </summary>
        public static OutgoingFailureKind? Check(string source, string? translated, string prefix, out string line, int maxBytes = MaxChatBytes)
        {
            line = string.Empty;
            var text = Sanitize(translated);

            if (text.Length == 0)
                return OutgoingFailureKind.Empty;

            if (text[0] is '/' or '／')
                return OutgoingFailureKind.StartsWithSlash;

            if (text == Sanitize(source))
                return OutgoingFailureKind.Unchanged;

            var candidate = BuildLine(prefix, text);
            if (Encoding.UTF8.GetByteCount(candidate) > maxBytes)
                return OutgoingFailureKind.TooLong;

            line = candidate;
            return null;
        }

        /// <summary>Builds the line for sending the original text (SendOriginal), or null when it would not fit.</summary>
        public static string? BuildOriginalLine(string source, string prefix, int maxBytes = MaxChatBytes)
        {
            var line = BuildLine(prefix, Sanitize(source));
            return Encoding.UTF8.GetByteCount(line) > maxBytes ? null : line;
        }

        private static string BuildLine(string prefix, string text) =>
            string.IsNullOrEmpty(prefix) ? text : $"{prefix} {text}";

        public static string Describe(OutgoingFailureKind kind) => kind switch
        {
            OutgoingFailureKind.Timeout => "the translation timed out",
            OutgoingFailureKind.EngineFailed => "the translation engine failed",
            OutgoingFailureKind.Empty => "the translation was empty",
            OutgoingFailureKind.StartsWithSlash => "the translation started with '/' and would have run as a command",
            OutgoingFailureKind.TooLong => "the translation is longer than the chat limit",
            OutgoingFailureKind.Unchanged => "the translation came back unchanged",
            OutgoingFailureKind.Cancelled => "you cancelled it",
            OutgoingFailureKind.ConfirmationTimeout => "the preview was not confirmed in time",
            _ => kind.ToString(),
        };
    }
}
