using System;
using System.Collections.Generic;

namespace EchoXIV
{
    /// <summary>
    /// Incoming translations printed into the game chat as a local-only line below the original,
    /// e.g. "[KT] Stack in the middle". The original line is never changed.
    /// </summary>
    public static class IncomingChatEcho
    {
        public const string Prefix = "[KT]";

        /// <summary>
        /// The line to print, or null when there is nothing worth printing: failed, outgoing,
        /// empty, or identical to the original (same-language lines, auto-translate-only lines).
        /// </summary>
        public static string? Format(string original, string? translated, bool failed, bool outgoing)
        {
            if (failed || outgoing || string.IsNullOrWhiteSpace(translated))
                return null;

            var text = translated.Trim();
            if (string.Equals(text, original.Trim(), StringComparison.Ordinal))
                return null;

            return $"{Prefix} {text}";
        }
    }

    /// <summary>
    /// Remembers lines Kotodama printed itself so the chat listener can skip them instead of
    /// translating its own output. Entries expire, in case a print never comes back as a message.
    /// Thread-safe.
    /// </summary>
    public sealed class OwnPrintRegistry
    {
        private readonly object _lock = new();
        private readonly List<(string Text, DateTimeOffset At)> _entries = new();
        private readonly TimeSpan _lifetime;
        private readonly Func<DateTimeOffset> _now;

        public OwnPrintRegistry(TimeSpan? lifetime = null, Func<DateTimeOffset>? now = null)
        {
            _lifetime = lifetime ?? TimeSpan.FromSeconds(30);
            _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        public void Register(string text)
        {
            lock (_lock)
            {
                Prune();
                _entries.Add((text, _now()));
            }
        }

        /// <summary>True (and forgets one entry) when <paramref name="text"/> is a line we printed.</summary>
        public bool Take(string text)
        {
            lock (_lock)
            {
                Prune();
                var index = _entries.FindIndex(e => string.Equals(e.Text, text, StringComparison.Ordinal));
                if (index < 0)
                    return false;

                _entries.RemoveAt(index);
                return true;
            }
        }

        private void Prune()
        {
            var cutoff = _now() - _lifetime;
            _entries.RemoveAll(e => e.At < cutoff);
        }
    }
}
