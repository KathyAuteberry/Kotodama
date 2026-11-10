using System.Collections.Concurrent;
using System.Text;
using EchoXIV.Outgoing;
using Xunit;

namespace Kotodama.Tests;

/// <summary>Translation preview (docs/PLAN.md P1.6): the queue head waits for confirmation.</summary>
public sealed class ConfirmationTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly EchoTranslator _translator = new();
    private readonly RecordingSink _sink = new();
    private readonly ConfirmationGate _gate = new();
    private OutgoingQueue? _queue;
    private bool _require = true;

    public void Dispose() => _queue?.Dispose();

    private OutgoingQueue Queue(TimeSpan? confirmationTimeout = null, OutgoingFailurePolicy policy = OutgoingFailurePolicy.DoNotSend)
    {
        _queue = new OutgoingQueue(_translator, _sink, new OutgoingQueueOptions
        {
            Timeout = () => TimeSpan.FromSeconds(5),
            FailurePolicy = () => policy,
            Confirmer = _gate,
            RequireConfirmation = () => _require,
            ConfirmationTimeout = () => confirmationTimeout ?? TimeSpan.FromSeconds(30),
        });
        return _queue;
    }

    private static OutgoingItem Translate(string text) =>
        new() { Kind = OutgoingItemKind.Translate, Text = text, Prefix = "/p" };

    private static OutgoingItem PassThrough(string text) =>
        new() { Kind = OutgoingItemKind.PassThrough, Text = text, Prefix = "/p" };

    private async Task<PendingConfirmation> WaitForPending(long? notId = null)
    {
        PendingConfirmation? pending = null;
        await WaitUntil(() => (pending = _gate.Current) != null && pending.Id != notId);
        return pending!;
    }

    [Fact]
    public async Task Translation_waits_for_confirmation_and_is_sent_when_confirmed()
    {
        var queue = Queue();
        queue.TryEnqueue(Translate("hello"));

        var pending = await WaitForPending();
        Assert.Equal("/p [hello]", pending.Send.Line);
        await Task.Delay(100);
        Assert.Empty(_sink.Sent);

        Assert.True(_gate.Resolve(pending.Id, send: true));
        await _sink.WaitForSends(1);
        Assert.Equal(new[] { "/p [hello]" }, _sink.Lines);
        Assert.Null(_gate.Current);
    }

    [Fact]
    public async Task Cancel_drops_the_item_and_never_sends_the_original()
    {
        var queue = Queue(policy: OutgoingFailurePolicy.SendOriginal);
        queue.TryEnqueue(Translate("hello"));

        var pending = await WaitForPending();
        _gate.Resolve(pending.Id, send: false);

        var failure = await _sink.WaitForFailure();
        Assert.Equal(OutgoingFailureKind.Cancelled, failure.Reason);
        Assert.False(failure.SentOriginal);
        await WaitUntil(() => queue.IsIdle);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task Later_items_wait_behind_a_pending_confirmation()
    {
        var queue = Queue();
        queue.TryEnqueue(Translate("first"));
        queue.TryEnqueue(PassThrough("plain"));
        queue.TryEnqueue(Translate("second"));

        var first = await WaitForPending();
        Assert.Equal("first", first.Send.Item.Text);
        await Task.Delay(100);
        Assert.Empty(_sink.Sent);
        Assert.Equal(1, _translator.Calls);

        _gate.Resolve(first.Id, send: false);
        var second = await WaitForPending(notId: first.Id);
        Assert.Equal("second", second.Send.Item.Text);
        Assert.Equal(new[] { "/p plain" }, _sink.Lines);

        _gate.Resolve(second.Id, send: true);
        await _sink.WaitForSends(2);
        Assert.Equal(new[] { "/p plain", "/p [second]" }, _sink.Lines);
    }

    [Fact]
    public async Task Unanswered_preview_times_out_as_cancel()
    {
        var queue = Queue(confirmationTimeout: TimeSpan.FromMilliseconds(150), policy: OutgoingFailurePolicy.SendOriginal);
        queue.TryEnqueue(Translate("hello"));
        queue.TryEnqueue(PassThrough("after"));

        var failure = await _sink.WaitForFailure();
        Assert.Equal(OutgoingFailureKind.ConfirmationTimeout, failure.Reason);
        Assert.False(failure.SentOriginal);
        await _sink.WaitForSends(1);
        Assert.Equal(new[] { "/p after" }, _sink.Lines);
        Assert.Null(_gate.Current);
    }

    [Fact]
    public async Task Late_answer_for_an_old_preview_cannot_confirm_the_next_one()
    {
        var queue = Queue();
        queue.TryEnqueue(Translate("first"));
        queue.TryEnqueue(Translate("second"));

        var first = await WaitForPending();
        Assert.True(_gate.Resolve(first.Id, send: false));
        Assert.False(_gate.Resolve(first.Id, send: true));

        var second = await WaitForPending(notId: first.Id);
        Assert.False(_gate.Resolve(first.Id, send: true));
        await Task.Delay(100);
        Assert.Empty(_sink.Sent);
        Assert.Same(second, _gate.Current);
    }

    [Fact]
    public async Task Disabled_preview_sends_without_asking()
    {
        _require = false;
        var queue = Queue();
        queue.TryEnqueue(Translate("hello"));

        await _sink.WaitForSends(1);
        Assert.Null(_gate.Current);
    }

    [Fact]
    public async Task Pass_through_and_failed_translations_are_never_previewed()
    {
        var queue = Queue();
        _translator.Results["same"] = "same";
        queue.TryEnqueue(PassThrough("plain"));
        queue.TryEnqueue(Translate("same"));

        await _sink.WaitForFailure();
        await WaitUntil(() => queue.IsIdle);
        Assert.Equal(new[] { "/p plain" }, _sink.Lines);
        Assert.Equal(OutgoingFailureKind.Unchanged, _sink.Failures.Single().Reason);
        Assert.Null(_gate.Current);
    }

    [Fact]
    public async Task Dispose_while_waiting_sends_nothing_and_reports_nothing()
    {
        var queue = Queue();
        queue.TryEnqueue(Translate("hello"));
        var pending = await WaitForPending();

        queue.Dispose();
        await queue.Completion.WaitAsync(Wait);
        Assert.False(_gate.Resolve(pending.Id, send: true));
        Assert.Empty(_sink.Sent);
        Assert.Empty(_sink.Failures);
        Assert.Null(_gate.Current);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }

    private sealed class EchoTranslator : IOutgoingTranslator
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ConcurrentDictionary<string, string> Results { get; } = new();

        public Task<OutgoingTranslation> TranslateAsync(OutgoingItem item, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var text = Results.TryGetValue(item.Text, out var result) ? result : $"[{item.Text}]";
            return Task.FromResult(new OutgoingTranslation(text, text, null));
        }
    }

    private sealed record Failure(OutgoingItem Item, OutgoingFailureKind Reason, bool SentOriginal);

    private sealed class RecordingSink : IOutgoingSink
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
