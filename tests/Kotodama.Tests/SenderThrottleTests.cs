using System;
using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public sealed class SenderThrottleTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    private DateTimeOffset _now = new(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);
    private readonly SenderThrottle _throttle;

    public SenderThrottleTests() => _throttle = new SenderThrottle(() => _now);

    private ThrottleDecision Line(string sender = "Leader@World", int max = 3) =>
        _throttle.Check(sender, max, Window, Cooldown);

    private void Advance(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Lines_below_the_limit_are_translated()
    {
        Assert.Equal(ThrottleDecision.Allow, Line());
        Advance(0.5);
        Assert.Equal(ThrottleDecision.Allow, Line());
    }

    [Fact]
    public void A_burst_pauses_the_sender_from_the_line_that_hits_the_limit()
    {
        Line();
        Advance(0.3);
        Line();
        Advance(0.3);
        Assert.Equal(ThrottleDecision.Suspended, Line());
        Advance(0.3);
        Assert.Equal(ThrottleDecision.StillSuspended, Line());
    }

    [Fact]
    public void A_long_macro_stays_paused_to_its_end()
    {
        Line(); Line(); Line();
        for (var i = 0; i < 20; i++)
        {
            Advance(5); // each gap shorter than the cooldown
            Assert.Equal(ThrottleDecision.StillSuspended, Line());
        }
    }

    [Fact]
    public void The_sender_resumes_after_a_quiet_cooldown()
    {
        Line(); Line(); Line();
        Advance(10.1);
        Assert.Equal(ThrottleDecision.Allow, Line());
        Advance(0.1);
        Assert.Equal(ThrottleDecision.Allow, Line());
    }

    [Fact]
    public void Slow_chat_is_never_paused()
    {
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(ThrottleDecision.Allow, Line());
            Advance(1.6); // two lines per window at most
        }
    }

    [Fact]
    public void Senders_are_counted_separately()
    {
        Line("A"); Line("B"); Line("A"); Line("B");
        Assert.Equal(ThrottleDecision.Suspended, Line("A"));
        Assert.Equal(ThrottleDecision.Suspended, Line("B"));
        Assert.Equal(ThrottleDecision.Allow, Line("C"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_limit_below_two_disables_the_throttle(int max)
    {
        for (var i = 0; i < 10; i++)
            Assert.Equal(ThrottleDecision.Allow, Line(max: max));
    }

    [Fact]
    public void Lines_older_than_the_window_do_not_count()
    {
        Line();
        Advance(2);
        Line();
        Advance(1.5); // first line now outside the 3 s window
        Assert.Equal(ThrottleDecision.Allow, Line());
        Advance(0.1);
        Assert.Equal(ThrottleDecision.Suspended, Line());
    }
}
