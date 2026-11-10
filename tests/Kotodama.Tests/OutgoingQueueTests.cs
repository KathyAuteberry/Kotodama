using System.Collections.Concurrent;
using System.Text;
using EchoXIV.Outgoing;
using Xunit;

namespace Kotodama.Tests;

public sealed class OutgoingQueueTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly FakeTranslator _translator = new();
    private readonly FakeSink _sink = new();
    private OutgoingQueue? _queue;

    public void Dispose() => _queue?.Dispose();

    private OutgoingQueue Queue(int capacity = 8, TimeSpan? timeout = null, OutgoingFailurePolicy policy = OutgoingFailurePolicy.DoNotSend)
    {
        _queue = new OutgoingQueue(_translator, _sink, new OutgoingQueueOptions
        {
            Capacity = capacity,
            Timeout = () => timeout ?? TimeSpan.FromSeconds(5),
            FailurePolicy = () => policy,
        });
        return _queue;
    }

    private static OutgoingItem Translate(string text, string prefix = "/p") =>
        new() { Kind = OutgoingItemKind.Translate, Text = text, Prefix = prefix };

    private static OutgoingItem PassThrough(string text, string prefix = "/p") =>
        new() { Kind = OutgoingItemKind.PassThrough, Text = text, Prefix = prefix };

    [Fact]
    public async Task Lines_are_sent_in_entry_order_even_when_a_later_translation_is_faster()
    {
        var queue = Queue();
        _translator.Delays["slow"] = TimeSpan.FromMilliseconds(300);
        _translator.Delays["fast"] = TimeSpan.Zero;

        Assert.True(queue.TryEnqueue(Translate("slow")));
        Assert.True(queue.TryEnqueue(Translate("fast")));
        Assert.True(queue.TryEnqueue(PassThrough("plain")));

        await _sink.WaitForSends(3);
        Assert.Equal(new[] { "/p [slow]", "/p [fast]", "/p plain" }, _sink.Lines);
    }

    [Fact]
    public async Task Only_one_item_is_translated_at_a_time()
    {
        var queue = Queue();
        _translator.Delays["a"] = TimeSpan.FromMilliseconds(100);
        _translator.Delays["b"] = TimeSpan.FromMilliseconds(100);

        queue.TryEnqueue(Translate("a"));
        queue.TryEnqueue(Translate("b"));
        await _sink.WaitForSends(2);

        Assert.Equal(1, _translator.MaxConcurrency);
    }

    [Fact]
    public async Task Sequence_numbers_increase_in_entry_order()
    {
        var queue = Queue();
        var items = Enumerable.Range(0, 5).Select(i => PassThrough($"m{i}")).ToList();
        foreach (var item in items)
            Assert.True(queue.TryEnqueue(item));

        await _sink.WaitForSends(5);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, items.Select(i => i.Sequence));
        Assert.Equal(items.Select(i => i.Sequence), _sink.Sent.Select(s => s.Item.Sequence));
    }

    [Fact]
    public async Task Full_queue_rejects_new_items()
    {
        var queue = Queue(capacity: 2);
        _translator.Block = new TaskCompletionSource();

        Assert.True(queue.TryEnqueue(Translate("in-progress")));
        await _translator.WaitForCalls(1); // consumer took it out of the channel
        Assert.True(queue.TryEnqueue(Translate("q1")));
        Assert.True(queue.TryEnqueue(Translate("q2")));
        Assert.False(queue.TryEnqueue(Translate("overflow")));
        Assert.Equal(3, queue.PendingCount);

        _translator.Block.SetResult();
        await _sink.WaitForSends(3);
        Assert.DoesNotContain(_sink.Lines, l => l.Contains("overflow"));
    }

    [Fact]
    public async Task Idle_tracks_pending_items()
    {
        var queue = Queue();
        Assert.True(queue.IsIdle);
        _translator.Block = new TaskCompletionSource();

        queue.TryEnqueue(Translate("x"));
        Assert.False(queue.IsIdle);

        _translator.Block.SetResult();
        await _sink.WaitForSends(1);
        await WaitUntil(() => queue.IsIdle);
    }

    [Fact]
    public async Task Timeout_with_DoNotSend_sends_nothing_and_reports_the_original()
    {
        var queue = Queue(timeout: TimeSpan.FromMilliseconds(100));
        _translator.Block = new TaskCompletionSource(); // never completes

        var item = Translate("Please wait, my game crashed.");
        queue.TryEnqueue(item);

        var failure = await _sink.WaitForFailure();
        Assert.Same(item, failure.Item);
        Assert.Equal(OutgoingFailureKind.Timeout, failure.Reason);
        Assert.False(failure.SentOriginal);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task Engine_failure_with_SendOriginal_sends_the_original_to_the_captured_channel()
    {
        var queue = Queue(policy: OutgoingFailurePolicy.SendOriginal);
        _translator.Throw = new InvalidOperationException("all engines failed");

        queue.TryEnqueue(Translate("hello", "/fc"));

        var failure = await _sink.WaitForFailure();
        Assert.Equal(OutgoingFailureKind.EngineFailed, failure.Reason);
        Assert.True(failure.SentOriginal);
        Assert.Equal(new[] { "/fc hello" }, _sink.Lines);
    }

    [Fact]
    public async Task Failure_does_not_block_later_items()
    {
        var queue = Queue();
        _translator.Results["bad"] = "/xlplugins";

        queue.TryEnqueue(Translate("bad"));
        queue.TryEnqueue(Translate("good"));

        await _sink.WaitForSends(1);
        Assert.Equal(new[] { "/p [good]" }, _sink.Lines);
        Assert.Equal(OutgoingFailureKind.StartsWithSlash, (await _sink.WaitForFailure()).Reason);
    }

    [Theory]
    [InlineData("", OutgoingFailureKind.Empty)]
    [InlineData("/e hi", OutgoingFailureKind.StartsWithSlash)]
    [InlineData("same", OutgoingFailureKind.Unchanged)]
    public async Task Guard_failures_are_reported_not_sent(string translation, OutgoingFailureKind expected)
    {
        var queue = Queue();
        _translator.Results["same"] = translation;

        queue.TryEnqueue(Translate("same"));

        Assert.Equal(expected, (await _sink.WaitForFailure()).Reason);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task Over_long_translation_is_reported_not_truncated()
    {
        var queue = Queue();
        _translator.Results["long"] = new string('あ', 200); // 600 UTF-8 bytes

        queue.TryEnqueue(Translate("long"));

        Assert.Equal(OutgoingFailureKind.TooLong, (await _sink.WaitForFailure()).Reason);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task Pass_through_raw_bytes_are_forwarded_unchanged()
    {
        var queue = Queue();
        var raw = new byte[] { (byte)'h', 0x02, 0x2E, 0x03, (byte)'i' };

        queue.TryEnqueue(new OutgoingItem { Kind = OutgoingItemKind.PassThrough, Text = "hi", RawLine = raw });

        await _sink.WaitForSends(1);
        Assert.Same(raw, _sink.Sent[0].RawLine);
        Assert.Null(_sink.Sent[0].Line);
    }

    [Fact]
    public async Task Nothing_is_sent_after_dispose()
    {
        var queue = Queue();
        _translator.Block = new TaskCompletionSource();

        queue.TryEnqueue(Translate("in-flight"));
        await _translator.WaitForCalls(1);
        queue.TryEnqueue(Translate("queued"));
        queue.TryEnqueue(PassThrough("queued plain"));

        queue.Dispose();
        _translator.Block.SetResult();
        await queue.Completion.WaitAsync(Wait);
        await Task.Delay(50);

        Assert.Empty(_sink.Sent);
        Assert.Empty(_sink.Failures);
        Assert.True(_translator.LastToken.IsCancellationRequested);
        Assert.False(queue.TryEnqueue(PassThrough("after")));
    }

    [Fact]
    public async Task Translated_send_carries_the_translation_details()
    {
        var queue = Queue();
        queue.TryEnqueue(Translate("stack"));

        await _sink.WaitForSends(1);
        Assert.Equal("[stack]", _sink.Sent[0].Translation!.NativeText);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class FakeTranslator : IOutgoingTranslator
    {
        public ConcurrentDictionary<string, TimeSpan> Delays { get; } = new();
        public ConcurrentDictionary<string, string> Results { get; } = new();
        public TaskCompletionSource? Block { get; set; }
        public Exception? Throw { get; set; }
        public CancellationToken LastToken { get; private set; }
        public int MaxConcurrency => _max;

        private int _calls;
        private int _active;
        private int _max;

        public async Task<OutgoingTranslation> TranslateAsync(OutgoingItem item, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            Interlocked.Increment(ref _calls);
            var active = Interlocked.Increment(ref _active);
            InterlockedMax(ref _max, active);
            try
            {
                if (Block != null)
                    await Block.Task.WaitAsync(cancellationToken);
                if (Delays.TryGetValue(item.Text, out var delay))
                    await Task.Delay(delay, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (Throw != null)
                    throw Throw;

                var text = Results.TryGetValue(item.Text, out var result) ? result : $"[{item.Text}]";
                return new OutgoingTranslation(text, text, null);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public async Task WaitForCalls(int count)
        {
            await WaitUntil(() => Volatile.Read(ref _calls) >= count);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value &&
                   Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    private sealed record Failure(OutgoingItem Item, OutgoingFailureKind Reason, bool SentOriginal);

    private sealed class FakeSink : IOutgoingSink
    {
        private readonly object _gate = new();
        private readonly List<OutgoingSend> _sent = new();
        private readonly List<Failure> _failures = new();

        public IReadOnlyList<OutgoingSend> Sent { get { lock (_gate) return _sent.ToList(); } }
        public IReadOnlyList<Failure> Failures { get { lock (_gate) return _failures.ToList(); } }
        public IReadOnlyList<string> Lines => Sent.Select(s => s.Line ?? Encoding.UTF8.GetString(s.RawLine!)).ToList();

        public Task SendAsync(OutgoingSend send)
        {
            lock (_gate) _sent.Add(send);
            return Task.CompletedTask;
        }

        public Task ReportFailureAsync(OutgoingItem item, OutgoingFailureKind reason, bool sentOriginal)
        {
            lock (_gate) _failures.Add(new Failure(item, reason, sentOriginal));
            return Task.CompletedTask;
        }

        public Task WaitForSends(int count) => WaitUntil(() => Sent.Count >= count);

        public async Task<Failure> WaitForFailure()
        {
            await WaitUntil(() => Failures.Count > 0);
            return Failures[0];
        }
    }
}
