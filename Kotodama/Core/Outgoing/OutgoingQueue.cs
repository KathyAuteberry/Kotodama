using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EchoXIV.Outgoing
{
    public enum OutgoingItemKind
    {
        /// <summary>Translate <see cref="OutgoingItem.Text"/>, then send it with <see cref="OutgoingItem.Prefix"/>.</summary>
        Translate,

        /// <summary>Send unchanged: <see cref="OutgoingItem.RawLine"/> if set, otherwise prefix + text.</summary>
        PassThrough,
    }

    /// <summary>One entered line waiting to be sent, with everything captured at entry time.</summary>
    public sealed class OutgoingItem
    {
        public required OutgoingItemKind Kind { get; init; }

        /// <summary>The message body as entered (without the channel prefix).</summary>
        public required string Text { get; init; }

        /// <summary>Explicit channel command captured at entry, e.g. "/p". Empty when <see cref="RawLine"/> already carries one.</summary>
        public string Prefix { get; init; } = string.Empty;

        /// <summary>Exact bytes of a pass-through line (keeps SeString payloads such as auto-translate phrases).</summary>
        public byte[]? RawLine { get; init; }

        /// <summary>Whether the game should put the sent line into chat input history.</summary>
        public bool SaveToHistory { get; init; }

        /// <summary>Assigned by <see cref="OutgoingQueue.TryEnqueue"/>; strictly increasing per queue.</summary>
        public long Sequence { get; internal set; }
    }

    public sealed record OutgoingTranslation(string TextToSend, string NativeText, string? RomanizedText);

    public interface IOutgoingTranslator
    {
        Task<OutgoingTranslation> TranslateAsync(OutgoingItem item, CancellationToken cancellationToken);
    }

    /// <summary>A send request handed to the game side: either a text line or raw bytes.</summary>
    public sealed record OutgoingSend(OutgoingItem Item, string? Line, byte[]? RawLine, OutgoingTranslation? Translation);

    /// <summary>The game-facing side of the queue. Implementations marshal to the framework thread.</summary>
    public interface IOutgoingSink
    {
        Task SendAsync(OutgoingSend send);

        /// <param name="sentOriginal">True when the SendOriginal policy sent the untranslated text.</param>
        Task ReportFailureAsync(OutgoingItem item, OutgoingFailureKind reason, bool sentOriginal);
    }

    public sealed class OutgoingQueueOptions
    {
        public int Capacity { get; init; } = 8;

        /// <summary>Per-item translation timeout. Read for every item.</summary>
        public Func<TimeSpan> Timeout { get; init; } = () => TimeSpan.FromSeconds(10);

        /// <summary>Failure policy. Read when a failure happens.</summary>
        public Func<OutgoingFailurePolicy> FailurePolicy { get; init; } = () => OutgoingFailurePolicy.DoNotSend;

        public int MaxLineBytes { get; init; } = OutgoingGuards.MaxChatBytes;
    }

    /// <summary>
    /// Bounded, per-session FIFO for outgoing chat (docs/PLAN.md P1.3). One consumer processes one
    /// item at a time, so lines reach the game exactly in the order they were entered, whatever
    /// order translations would finish in. Nothing is sent after <see cref="Dispose"/>.
    /// </summary>
    public sealed class OutgoingQueue : IDisposable
    {
        private readonly IOutgoingTranslator _translator;
        private readonly IOutgoingSink _sink;
        private readonly OutgoingQueueOptions _options;
        private readonly Channel<OutgoingItem> _channel;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _consumer;
        private long _nextSequence;
        private int _pending;
        private int _disposed;

        public OutgoingQueue(IOutgoingTranslator translator, IOutgoingSink sink, OutgoingQueueOptions? options = null)
        {
            _translator = translator;
            _sink = sink;
            _options = options ?? new OutgoingQueueOptions();
            _channel = Channel.CreateBounded<OutgoingItem>(new BoundedChannelOptions(_options.Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
            _consumer = Task.Run(ConsumeAsync);
        }

        /// <summary>True when no entered item is waiting or being processed.</summary>
        public bool IsIdle => Volatile.Read(ref _pending) == 0;

        /// <summary>Items entered but not yet finished.</summary>
        public int PendingCount => Volatile.Read(ref _pending);

        /// <summary>
        /// Adds an item without blocking. Returns false when the queue is full or disposed; the
        /// caller must then tell the user the line was not sent.
        /// </summary>
        public bool TryEnqueue(OutgoingItem item)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;

            lock (_channel)
            {
                Interlocked.Increment(ref _pending);
                item.Sequence = ++_nextSequence;
                if (_channel.Writer.TryWrite(item))
                    return true;

                _nextSequence--;
                Interlocked.Decrement(ref _pending);
                return false;
            }
        }

        /// <summary>Completes when the consumer has stopped (after <see cref="Dispose"/>). For tests.</summary>
        public Task Completion => _consumer;

        private async Task ConsumeAsync()
        {
            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await ProcessAsync(item).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        // A sink failure must not stop the queue; the item is dropped.
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pending);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed.
            }
        }

        private async Task ProcessAsync(OutgoingItem item)
        {
            if (_lifetime.IsCancellationRequested)
                return;

            if (item.Kind == OutgoingItemKind.PassThrough)
            {
                var line = item.RawLine == null ? JoinPrefix(item.Prefix, OutgoingGuards.Sanitize(item.Text)) : null;
                await _sink.SendAsync(new OutgoingSend(item, line, item.RawLine, null)).ConfigureAwait(false);
                return;
            }

            OutgoingTranslation translation;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
            {
                timeout.CancelAfter(_options.Timeout());
                try
                {
                    translation = await _translator.TranslateAsync(item, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    await FailAsync(item, OutgoingFailureKind.Timeout).ConfigureAwait(false);
                    return;
                }
                catch (Exception)
                {
                    await FailAsync(item, OutgoingFailureKind.EngineFailed).ConfigureAwait(false);
                    return;
                }
            }

            if (_lifetime.IsCancellationRequested)
                return;

            var failure = OutgoingGuards.Check(item.Text, translation.TextToSend, item.Prefix, out var translatedLine, _options.MaxLineBytes);
            if (failure != null)
            {
                await FailAsync(item, failure.Value).ConfigureAwait(false);
                return;
            }

            await _sink.SendAsync(new OutgoingSend(item, translatedLine, null, translation)).ConfigureAwait(false);
        }

        private async Task FailAsync(OutgoingItem item, OutgoingFailureKind reason)
        {
            if (_lifetime.IsCancellationRequested)
                return;

            var sentOriginal = false;
            if (_options.FailurePolicy() == OutgoingFailurePolicy.SendOriginal)
            {
                var original = OutgoingGuards.BuildOriginalLine(item.Text, item.Prefix, _options.MaxLineBytes);
                if (original != null)
                {
                    await _sink.SendAsync(new OutgoingSend(item, original, null, null)).ConfigureAwait(false);
                    sentOriginal = true;
                }
            }

            await _sink.ReportFailureAsync(item, reason, sentOriginal).ConfigureAwait(false);
        }

        private static string JoinPrefix(string prefix, string text) =>
            string.IsNullOrEmpty(prefix) ? text : $"{prefix} {text}";

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _lifetime.Cancel();
            _channel.Writer.TryComplete();
            // The consumer observes cancellation; the CTS is left for the GC so a racing
            // CreateLinkedTokenSource never touches a disposed source.
        }
    }
}
