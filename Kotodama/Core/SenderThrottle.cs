using System;
using System.Collections.Generic;

namespace EchoXIV
{
    public enum ThrottleDecision
    {
        /// <summary>Translate the message.</summary>
        Allow,

        /// <summary>This message started a burst: skip it, and the sender is now paused.</summary>
        Suspended,

        /// <summary>The sender is already paused: skip it.</summary>
        StillSuspended,
    }

    /// <summary>
    /// Pauses translation of a sender who posts a burst of lines (raid macros): once a sender has
    /// <c>MaxMessages</c> lines within <c>Window</c>, that line and everything after it is skipped
    /// until the sender has been quiet for <c>Cooldown</c>. Every skipped line restarts the cooldown,
    /// so a long macro stays paused to its end. Thread-safe; never blocks beyond a short lock.
    /// </summary>
    public sealed class SenderThrottle
    {
        private sealed class SenderState
        {
            public readonly Queue<DateTimeOffset> Recent = new();
            public DateTimeOffset? SuspendedUntil;
            public DateTimeOffset LastSeen;
        }

        private readonly object _lock = new();
        private readonly Dictionary<string, SenderState> _senders = new(StringComparer.Ordinal);
        private readonly Func<DateTimeOffset> _now;

        public SenderThrottle(Func<DateTimeOffset>? now = null)
        {
            _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Records one line from <paramref name="sender"/> and decides whether to translate it.
        /// <paramref name="maxMessages"/> below 2 disables the throttle.
        /// </summary>
        public ThrottleDecision Check(string sender, int maxMessages, TimeSpan window, TimeSpan cooldown)
        {
            if (maxMessages < 2 || string.IsNullOrEmpty(sender))
                return ThrottleDecision.Allow;

            var now = _now();
            lock (_lock)
            {
                Prune(now, window, cooldown);

                if (!_senders.TryGetValue(sender, out var state))
                {
                    state = new SenderState();
                    _senders[sender] = state;
                }

                state.LastSeen = now;

                if (state.SuspendedUntil is { } until)
                {
                    if (now < until)
                    {
                        state.SuspendedUntil = now + cooldown;
                        return ThrottleDecision.StillSuspended;
                    }

                    // Quiet long enough: start counting afresh.
                    state.SuspendedUntil = null;
                    state.Recent.Clear();
                }

                while (state.Recent.Count > 0 && now - state.Recent.Peek() > window)
                    state.Recent.Dequeue();

                state.Recent.Enqueue(now);
                if (state.Recent.Count < maxMessages)
                    return ThrottleDecision.Allow;

                state.Recent.Clear();
                state.SuspendedUntil = now + cooldown;
                return ThrottleDecision.Suspended;
            }
        }

        /// <summary>Forgets senders not seen for longer than both the window and the cooldown.</summary>
        private void Prune(DateTimeOffset now, TimeSpan window, TimeSpan cooldown)
        {
            if (_senders.Count < 64)
                return;

            var idle = window > cooldown ? window : cooldown;
            List<string>? stale = null;
            foreach (var (name, state) in _senders)
            {
                if (now - state.LastSeen > idle)
                    (stale ??= new()).Add(name);
            }

            if (stale == null)
                return;
            foreach (var name in stale)
                _senders.Remove(name);
        }
    }
}
