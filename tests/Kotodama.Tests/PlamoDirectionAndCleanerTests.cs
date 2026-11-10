using EchoXIV.Plamo;
using Xunit;

namespace Kotodama.Tests;

public class DirectionResolverTests
{
    [Theory]
    [InlineData("MT無敵", TranslationDirection.JaToEn)]
    [InlineData("ｵｹ", TranslationDirection.JaToEn)]
    public void Incoming_japanese_is_translated(string text, TranslationDirection expected)
    {
        Assert.Equal(expected, DirectionResolver.ForIncoming(text));
    }

    [Theory]
    [InlineData("stack")]
    [InlineData("o/")]
    [InlineData("；；")]
    public void Incoming_english_or_letterless_lines_are_not_translated(string text)
    {
        Assert.Null(DirectionResolver.ForIncoming(text));
    }

    [Fact]
    public void Outgoing_uses_the_configured_direction_without_detection()
    {
        Assert.Equal(TranslationDirection.EnToJa, DirectionResolver.ForOutgoing(TranslationDirection.EnToJa, detect: false, "散開"));
    }

    [Theory]
    [InlineData("散開", TranslationDirection.JaToEn)]
    [InlineData("stack", TranslationDirection.EnToJa)]
    [InlineData("o/", null)]
    public void Outgoing_detection_uses_the_message_script(string text, TranslationDirection? expected)
    {
        Assert.Equal(expected, DirectionResolver.ForOutgoing(TranslationDirection.EnToJa, detect: true, text));
    }

    [Theory]
    [InlineData("en", "軽減お願いします", TranslationDirection.JaToEn)]
    [InlineData("ja", "Please wait, my game crashed.", TranslationDirection.EnToJa)]
    [InlineData("JA", "stack", TranslationDirection.EnToJa)]
    public void Target_language_maps_to_a_direction(string target, string text, TranslationDirection expected)
    {
        Assert.Equal(expected, DirectionResolver.ForTargetLanguage(target, text, out var supported));
        Assert.True(supported);
    }

    [Theory]
    [InlineData("en", "stack")]
    [InlineData("ja", "散開")]
    [InlineData("en", "o/")]
    public void Text_already_in_the_target_language_needs_no_translation(string target, string text)
    {
        Assert.Null(DirectionResolver.ForTargetLanguage(target, text, out var supported));
        Assert.True(supported);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("zh-CN")]
    [InlineData("")]
    public void Other_target_languages_are_unsupported(string target)
    {
        Assert.Null(DirectionResolver.ForTargetLanguage(target, "hello", out var supported));
        Assert.False(supported);
    }
}

public class PlamoOutputCleanerTests
{
    [Theory]
    [InlineData("  Please use mitigation.  ", "Please use mitigation.")]
    [InlineData("Please use mitigation.<|plamo:tag|>input<|plamo:msg|>次", "Please use mitigation.")]
    [InlineData("Please use mitigation.\n\nAlternatively: Mitigation please.", "Please use mitigation.")]
    [InlineData("\n\nFirst line\nsecond", "First line")]
    [InlineData("\"Please use mitigation.\"", "Please use mitigation.")]
    [InlineData("「ご飯切れてないか確認お願いします」", "ご飯切れてないか確認お願いします")]
    [InlineData("“quoted”", "quoted")]
    [InlineData("", "")]
    public void Keeps_only_the_first_translation(string output, string expected)
    {
        Assert.Equal(expected, PlamoOutputCleaner.Clean(output, "軽減お願いします"));
    }

    [Fact]
    public void Quotes_the_user_wrote_are_kept()
    {
        Assert.Equal("\"GG\"", PlamoOutputCleaner.Clean("\"GG\"", "\"GG\""));
        Assert.Equal("「了解」", PlamoOutputCleaner.Clean("「了解」", "「了解」"));
    }

    [Fact]
    public void Multi_line_sources_keep_multiple_lines()
    {
        Assert.Equal("a\nb", PlamoOutputCleaner.Clean("a\nb", "x\ny"));
    }

    [Fact]
    public void Null_output_is_empty()
    {
        Assert.Equal(string.Empty, PlamoOutputCleaner.Clean(null, "x"));
    }
}
