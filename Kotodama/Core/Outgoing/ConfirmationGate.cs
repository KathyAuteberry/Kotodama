using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoXIV.Outgoing
{
    /// <summary>A translation waiting for the user's decision.</summary>
    public sealed record PendingConfirmation(long Id, OutgoingSend Send, DateTimeOffset Deadline);

    /// <summary>
    /// Holds the one translation awaiting confirmation (docs/PLAN.md P1.6). The queue's consumer
    /// waits in <see cref="ConfirmAsync"/>; the preview window polls <see cref="Current"/> on the
    /// framework thread and calls <see cref="Resolve"/>. Nothing here blocks the caller of
    /// <see cref="Current"/> or <see cref="Resolve"/>.
    /// </summary>
    public sealed class ConfirmationGate : IOutgoingConfirmer
    {
        private readonly object _lock = new();
        private PendingConfirmation? _current;
        private TaskCompletionSource<bool>? _decision;
        private long _nextId;

        /// <summary>The translation waiting for a decision, or null.</summary>
        public PendingConfirmation? Current
        {
            get { lock (_lock) return _current; }
        }

        public async Task<bool> ConfirmAsync(OutgoingSend send, DateTimeOffset deadline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Continuations must not run on the thread that resolves (the framework thread).
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingConfirmation pending;
            lock (_lock)
            {
                if (_decision != null)
                    throw new InvalidOperationException("Only one confirmation can be pending.");

                pending = new PendingConfirmation(++_nextId, send, deadline);
                _current = pending;
                _decision = decision;
            }

            try
            {
                using (cancellationToken.Register(() => decision.TrySetCanceled(cancellationToken)))
                    return await decision.Task.ConfigureAwait(false);
            }
            finally
            {
                lock (_lock)
                {
                    if (_current?.Id == pending.Id)
                    {
                        _current = null;
                        _decision = null;
                    }
                }
            }
        }

        /// <summary>
        /// Answers the pending confirmation with id <paramref name="id"/>. Returns false when it
        /// is no longer pending (already answered, timed out, or a newer one replaced it), so a
        /// late key press can never confirm the next line.
        /// </summary>
        public bool Resolve(long id, bool send)
        {
            TaskCompletionSource<bool>? decision;
            lock (_lock)
            {
                if (_current?.Id != id)
                    return false;

                decision = _decision;
                _current = null;
                _decision = null;
            }

            return decision?.TrySetResult(send) ?? false;
        }
    }
}
