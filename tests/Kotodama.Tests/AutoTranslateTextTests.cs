using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public class AutoTranslateTextTests
{
    private static ChatSegment T(string text) => new(false, text);
    private static ChatSegment AT(string phrase) => new(true, $" {phrase} ");

    [Fact]
    public void Phrase_only_line_needs_no_translation()
    {
        var split = AutoTranslateText.Split(new[] { AT("Looking forward to working with you.") });

        Assert.True(split.OnlyAutoTranslate);
        Assert.Equal(new[] { "Looking forward to working with you." }, split.Phrases);
        Assert.Equal("【Looking forward to working with you.】", AutoTranslateText.Compose(split.Phrases, string.Empty));
    }

    [Theory]
    [InlineData("!")]
    [InlineData(" ；； ")]
    [InlineData(" o/")]
    public void Phrases_with_only_punctuation_or_emotes_need_no_translation(string extra)
    {
        Assert.True(AutoTranslateText.Split(new[] { AT("Hello!"), T(extra) }).OnlyAutoTranslate);
    }

    [Fact]
    public void Mixed_line_translates_only_the_text_and_keeps_phrases_verbatim()
    {
        var split = AutoTranslateText.Split(new[] { AT("Hello!"), T("今日は"), AT("Party Finder"), T("よろしく") });

        Assert.False(split.OnlyAutoTranslate);
        Assert.Equal("今日は よろしく", split.Translatable);
        Assert.Equal(new[] { "Hello!", "Party Finder" }, split.Phrases);
        Assert.Equal("【Hello!】 【Party Finder】 Thanks for today", AutoTranslateText.Compose(split.Phrases, "Thanks for today"));
    }

    [Fact]
    public void Plain_line_is_unchanged()
    {
        var split = AutoTranslateText.Split(new[] { T("軽減お願いします") });

        Assert.False(split.OnlyAutoTranslate);
        Assert.Empty(split.Phrases);
        Assert.Equal("軽減お願いします", split.Translatable);
        Assert.Equal("Please use mitigation.", AutoTranslateText.Compose(split.Phrases, "Please use mitigation."));
    }

    [Fact]
    public void Glyphs_and_spaces_are_removed_from_phrases()
    {
        Assert.Equal("Tank", AutoTranslateText.CleanPhrase(" Tank "));
        Assert.Equal(string.Empty, AutoTranslateText.CleanPhrase(""));
    }

    [Fact]
    public void Empty_phrases_are_ignored()
    {
        var split = AutoTranslateText.Split(new[] { new ChatSegment(true, " "), T("散開") });

        Assert.Empty(split.Phrases);
        Assert.Equal("散開", split.Translatable);
    }
}
