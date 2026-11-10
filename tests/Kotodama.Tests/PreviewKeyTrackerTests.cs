using EchoXIV.Outgoing;
using Xunit;

namespace Kotodama.Tests;

public sealed class PreviewKeyTrackerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private readonly PreviewKeyTracker _tracker = new(TimeSpan.FromMilliseconds(250));

    private PreviewKeyResult Frame(long? id, int ms, bool enter = false, bool escape = false, bool typing = false) =>
        _tracker.Update(new PreviewKeyInput(id, enter, escape, typing, T0.AddMilliseconds(ms)));

    [Fact]
    public void Fresh_enter_after_grace_sends_and_is_hidden_from_the_game()
    {
        Frame(1, 0);
        var result = Frame(1, 300, enter: true);

        Assert.Equal(1, result.Id);
        Assert.True(result.Answer);
        Assert.True(result.SwallowEnter);
    }

    [Fact]
    public void Fresh_escape_cancels_and_is_hidden_from_the_game()
    {
        Frame(1, 0);
        var result = Frame(1, 300, escape: true);

        Assert.False(result.Answer);
        Assert.True(result.SwallowEscape);
        Assert.False(result.SwallowEnter);
    }

    [Fact]
    public void Enter_held_from_sending_the_line_does_not_answer()
    {
        Assert.Null(Frame(1, 0, enter: true).Answer);
        Assert.Null(Frame(1, 400, enter: true).Answer);
        Assert.True(Frame(1, 400, enter: true).SwallowEnter);

        Frame(1, 450);
        Assert.True(Frame(1, 500, enter: true).Answer);
    }

    [Fact]
    public void Press_within_the_grace_period_does_not_answer()
    {
        Frame(1, 0);
        Assert.Null(Frame(1, 100, enter: true).Answer);
        Assert.Null(Frame(1, 300, enter: true).Answer); // still the same press
    }

    [Fact]
    public void Keys_belong_to_an_active_text_input()
    {
        Frame(1, 0);
        var typing = Frame(1, 300, enter: true, typing: true);
        Assert.Null(typing.Answer);
        Assert.False(typing.SwallowEnter);

        // The Enter that sent the chat line is still down when the input closes: not an answer.
        Assert.Null(Frame(1, 320, enter: true).Answer);
        Frame(1, 340);
        Assert.True(Frame(1, 360, enter: true).Answer);
    }

    [Fact]
    public void Answering_key_stays_hidden_until_released_even_after_the_preview_closes()
    {
        Frame(1, 0);
        Assert.True(Frame(1, 300, enter: true).Answer);

        var after = Frame(null, 320, enter: true);
        Assert.Null(after.Answer);
        Assert.True(after.SwallowEnter);

        Assert.False(Frame(null, 340).SwallowEnter);
        Assert.False(Frame(null, 360, enter: true).SwallowEnter);
    }

    [Fact]
    public void Held_enter_does_not_confirm_the_next_preview()
    {
        Frame(1, 0);
        Assert.True(Frame(1, 300, enter: true).Answer);

        // A cached translation shows the next preview while Enter is still held.
        Assert.Null(Frame(2, 320, enter: true).Answer);
        Assert.Null(Frame(2, 900, enter: true).Answer);
        Frame(2, 950);
        Assert.True(Frame(2, 1000, enter: true).Answer);
    }

    [Fact]
    public void Nothing_is_hidden_without_a_preview()
    {
        var result = Frame(null, 0, enter: true, escape: true);
        Assert.Null(result.Answer);
        Assert.False(result.SwallowEnter);
        Assert.False(result.SwallowEscape);
    }
}
