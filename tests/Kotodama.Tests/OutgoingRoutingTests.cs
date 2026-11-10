using EchoXIV.Outgoing;
using Xunit;

namespace Kotodama.Tests;

public class OutgoingRoutingTests
{
    private static OutgoingDecision Decide(
        OutgoingMode mode, string text, bool excluded = false, bool channelCommand = false,
        bool payload = false, bool queueEmpty = true) =>
        OutgoingRouting.Decide(mode, text, excluded, channelCommand, payload, queueEmpty);

    [Theory]
    [InlineData(OutgoingMode.Off)]
    [InlineData(OutgoingMode.ExplicitOnly)]
    [InlineData(OutgoingMode.AutoIntercept)]
    public void Empty_lines_bypass(OutgoingMode mode)
    {
        Assert.Equal(OutgoingDecision.Bypass, Decide(mode, ""));
        Assert.Equal(OutgoingDecision.Bypass, Decide(mode, "   ", queueEmpty: false));
    }

    [Theory]
    [InlineData(OutgoingMode.Off)]
    [InlineData(OutgoingMode.ExplicitOnly)]
    public void Plain_lines_go_straight_to_the_game_when_nothing_is_pending(OutgoingMode mode)
    {
        Assert.Equal(OutgoingDecision.Bypass, Decide(mode, "hello"));
    }

    [Theory]
    [InlineData(OutgoingMode.Off)]
    [InlineData(OutgoingMode.ExplicitOnly)]
    [InlineData(OutgoingMode.AutoIntercept)]
    public void Untranslated_lines_wait_behind_pending_items(OutgoingMode mode)
    {
        Assert.Equal(OutgoingDecision.PassThrough, Decide(mode, "o/", excluded: true, queueEmpty: false));
    }

    [Fact]
    public void ExplicitOnly_plain_line_waits_behind_a_pending_tl()
    {
        Assert.Equal(OutgoingDecision.PassThrough, Decide(OutgoingMode.ExplicitOnly, "hello", queueEmpty: false));
    }

    [Fact]
    public void AutoIntercept_translates_plain_lines()
    {
        Assert.Equal(OutgoingDecision.Translate, Decide(OutgoingMode.AutoIntercept, "hello"));
        Assert.Equal(OutgoingDecision.Translate, Decide(OutgoingMode.AutoIntercept, "hello", queueEmpty: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AutoIntercept_never_translates_excluded_or_payload_lines(bool excluded, bool payload)
    {
        Assert.Equal(OutgoingDecision.Bypass, Decide(OutgoingMode.AutoIntercept, "o/", excluded, payload: payload));
    }

    [Theory]
    [InlineData(OutgoingMode.ExplicitOnly)]
    [InlineData(OutgoingMode.AutoIntercept)]
    public void Non_chat_commands_always_bypass(OutgoingMode mode)
    {
        Assert.Equal(OutgoingDecision.Bypass, Decide(mode, "/tl hello", queueEmpty: false));
        Assert.Equal(OutgoingDecision.Bypass, Decide(mode, "/dance", queueEmpty: false));
    }

    [Theory]
    [InlineData(OutgoingMode.ExplicitOnly)]
    [InlineData(OutgoingMode.AutoIntercept)]
    public void Chat_channel_commands_are_never_translated_but_keep_order(OutgoingMode mode)
    {
        Assert.Equal(OutgoingDecision.Bypass, Decide(mode, "/p hi", channelCommand: true));
        Assert.Equal(OutgoingDecision.PassThrough, Decide(mode, "/p hi", channelCommand: true, queueEmpty: false));
    }
}
