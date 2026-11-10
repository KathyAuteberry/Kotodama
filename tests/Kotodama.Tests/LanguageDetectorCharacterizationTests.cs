using EchoXIV.Services;
using Xunit;

namespace Kotodama.Tests;

/// <summary>
/// Pins the current behaviour of the upstream detectors (docs/AUDIT.md, finding 9),
/// including answers that are wrong for JA/EN chat. These are characterization tests, not a
/// specification: the PLaMo path will use the EN/JA script classifier from docs/PLAN.md P3.2.
/// When that lands, the "known wrong" cases here document why the old detector was not reused.
/// </summary>
public class LanguageDetectorCharacterizationTests
{
    [Theory]
    [InlineData("MT無敵受けで")]
    [InlineData("D1とD2逆です")]
    [InlineData("蘇生飛ばしてます")]
    [InlineData("軽減お願いします")]
    [InlineData("LB3お願い")]
    public void Lines_with_kana_are_detected_as_confident_japanese(string text)
    {
        var detection = LanguageDetector.Detect(text);

        Assert.Equal("ja", detection.Code);
        Assert.True(detection.IsConfident(0.85));
        Assert.Equal(NonLatinScriptKind.Japanese, NonLatinScriptDetector.Detect(text));
    }

    [Fact]
    public void Known_wrong_kanji_only_japanese_with_latin_is_detected_as_confident_chinese()
    {
        var detection = LanguageDetector.Detect("MT無敵");

        Assert.Equal("zh-CN", detection.Code);
        Assert.Equal(0.88, detection.Confidence, precision: 2);
        Assert.True(detection.IsConfident(0.85));
        Assert.Equal(NonLatinScriptKind.ChineseSimplified, NonLatinScriptDetector.Detect("MT無敵"));
    }

    [Theory]
    [InlineData("散開")]
    [InlineData("無敵")]
    [InlineData("了解")]
    public void Known_wrong_short_kanji_only_japanese_is_unknown_and_scripted_as_chinese(string text)
    {
        Assert.Equal(default, LanguageDetector.Detect(text));
        Assert.Equal(NonLatinScriptKind.ChineseSimplified, NonLatinScriptDetector.Detect(text));
    }

    [Fact]
    public void Short_kana_line_is_unknown_because_it_has_fewer_than_four_letters()
    {
        Assert.Equal(default, LanguageDetector.Detect("頭割り"));
        Assert.Equal(NonLatinScriptKind.Japanese, NonLatinScriptDetector.Detect("頭割り"));
    }

    [Fact]
    public void Known_wrong_halfwidth_katakana_is_not_recognised()
    {
        Assert.Equal(default, LanguageDetector.Detect("ｵｹ"));
        Assert.Equal(NonLatinScriptKind.Latin, NonLatinScriptDetector.Detect("ｵｹ"));
    }

    [Theory]
    [InlineData("Please make sure your food hasn't expired.")]
    [InlineData("stack")]
    [InlineData("ty!")]
    [InlineData("o/")]
    public void Known_weak_many_english_lines_are_unknown(string text)
    {
        Assert.Equal(default, LanguageDetector.Detect(text));
        Assert.Equal(NonLatinScriptKind.Latin, NonLatinScriptDetector.Detect(text));
    }

    [Theory]
    [InlineData("Does D1 take the far tower?")]
    [InlineData("I'll MT; PLD can ST.")]
    [InlineData("That was close! ;w;")]
    public void Known_weak_short_english_is_below_the_default_confidence_threshold(string text)
    {
        var detection = LanguageDetector.Detect(text);

        Assert.Equal("en", detection.Code);
        Assert.False(detection.IsConfident(0.85));
    }

    [Fact]
    public void Longer_english_with_marker_words_is_confident()
    {
        var detection = LanguageDetector.Detect("You can just call me Kath.");

        Assert.Equal("en", detection.Code);
        Assert.True(detection.IsConfident(0.85));
    }
}
