using EchoXIV.Outgoing;
using Xunit;

namespace Kotodama.Tests;

public class OutgoingGuardsTests
{
    [Fact]
    public void Valid_translation_builds_the_prefixed_line()
    {
        Assert.Null(OutgoingGuards.Check("Please wait, my game crashed.", "すみません、ゲームが落ちました。", "/p", out var line));
        Assert.Equal("/p すみません、ゲームが落ちました。", line);
    }

    [Theory]
    [InlineData("line1\nline2", "/p line1 line2")]
    [InlineData("a\r\nb", "/p a  b")]
    [InlineData("  padded  ", "/p padded")]
    [InlineData("ctrl\u0007char", "/p ctrlchar")]
    public void Line_breaks_and_control_characters_are_sanitized(string translated, string expected)
    {
        Assert.Null(OutgoingGuards.Check("source", translated, "/p", out var line));
        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData("/xlplugins")]
    [InlineData("  /p hi")]
    [InlineData("／ｐ　こんにちは")]
    public void Translations_that_would_run_as_commands_are_rejected(string translated)
    {
        Assert.Equal(OutgoingFailureKind.StartsWithSlash, OutgoingGuards.Check("x", translated, "/p", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\u0000")]
    public void Empty_translations_are_rejected(string? translated)
    {
        Assert.Equal(OutgoingFailureKind.Empty, OutgoingGuards.Check("x", translated, "/p", out _));
    }

    [Fact]
    public void Unchanged_translation_is_rejected()
    {
        Assert.Equal(OutgoingFailureKind.Unchanged, OutgoingGuards.Check("Kath", " Kath ", "/p", out _));
    }

    [Fact]
    public void Length_counts_utf8_bytes_including_the_prefix()
    {
        var fits = new string('あ', 148);    // 444 bytes + "/p " = 447
        var tooLong = new string('あ', 150); // 450 bytes + "/p " = 453

        Assert.Null(OutgoingGuards.Check("x", fits, "/p", out _));
        Assert.Equal(OutgoingFailureKind.TooLong, OutgoingGuards.Check("x", tooLong, "/p", out _));
    }

    [Fact]
    public void Original_line_is_null_when_it_would_not_fit()
    {
        Assert.Equal("/fc hi", OutgoingGuards.BuildOriginalLine("hi", "/fc"));
        Assert.Null(OutgoingGuards.BuildOriginalLine(new string('x', 460), "/fc"));
    }

    [Fact]
    public void Every_failure_kind_has_a_description()
    {
        foreach (var kind in Enum.GetValues<OutgoingFailureKind>())
            Assert.False(string.IsNullOrWhiteSpace(OutgoingGuards.Describe(kind)));
    }
}
