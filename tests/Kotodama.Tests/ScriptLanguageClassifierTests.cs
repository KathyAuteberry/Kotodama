using EchoXIV.Plamo;
using Kotodama.Tests.Fixtures;
using Xunit;

namespace Kotodama.Tests;

public class ScriptLanguageClassifierTests
{
    [Theory]
    [InlineData("MT無敵")]
    [InlineData("MT無敵受けで")]
    [InlineData("蘇生飛ばしてます")]
    [InlineData("D1とD2逆です")]
    [InlineData("LB3お願い")]
    [InlineData("散開")]
    [InlineData("無敵")]
    [InlineData("了解")]
    [InlineData("草")]
    [InlineData("はい")]
    [InlineData("塔踏み忘れましたすみません；；")]
    public void Regression_and_bench_lines_are_japanese(string text)
    {
        Assert.Equal(ScriptLanguage.Ja, ScriptLanguageClassifier.Classify(text));
    }

    [Theory]
    [InlineData("Please make sure your food hasn't expired.")]
    [InlineData("You can just call me Kath.")]
    [InlineData("I'll MT; PLD can ST.")]
    [InlineData("stack")]
    [InlineData("Does D1 take the far tower?")]
    public void English_lines_are_english(string text)
    {
        Assert.Equal(ScriptLanguage.En, ScriptLanguageClassifier.Classify(text));
    }

    [Theory]
    [InlineData("Fulgent Bladeでスプリントお願いします", ScriptLanguage.Ja)]
    [InlineData("Can we do 頭割り at B?", ScriptLanguage.En)]
    [InlineData("/p MT無敵、ST誘導お願いします", ScriptLanguage.Ja)]
    public void Mixed_lines(string text, ScriptLanguage expected)
    {
        Assert.Equal(expected, ScriptLanguageClassifier.Classify(text));
    }

    [Theory]
    [InlineData("lol ¯\\_(ツ)_/¯")]
    [InlineData("ok ヽ(°〇°)ﾉ")]
    [InlineData("gg (ﾟ∀ﾟ)")]
    public void Kaomoji_kana_do_not_make_english_japanese(string text)
    {
        Assert.Equal(ScriptLanguage.En, ScriptLanguageClassifier.Classify(text));
    }

    [Theory]
    [InlineData("That’s “fine” — café…")]
    [InlineData("‚quoted„ «guillemets» it–is—fine")]
    public void English_typographic_punctuation_is_ignored(string text)
    {
        Assert.Equal(ScriptLanguage.En, ScriptLanguageClassifier.Classify(text));
    }

    [Theory]
    [InlineData("ＯＫ", ScriptLanguage.En)]
    [InlineData("ｗｗｗ", ScriptLanguage.En)]
    [InlineData("ｵｹ", ScriptLanguage.Ja)]
    [InlineData("ｽﾄｯﾌﾟ", ScriptLanguage.Ja)]
    public void Fullwidth_and_halfwidth_forms(string text, ScriptLanguage expected)
    {
        Assert.Equal(expected, ScriptLanguageClassifier.Classify(text));
    }

    [Theory]
    [InlineData("o/")]
    [InlineData("o7")]
    [InlineData("\\o/")]
    [InlineData("B")]
    [InlineData("；；")]
    [InlineData("…")]
    [InlineData("123")]
    [InlineData("!!")]
    [InlineData("😭😭")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("、。「」！？～・ー〇")]
    public void Lines_without_letters_are_unknown(string? text)
    {
        Assert.Equal(ScriptLanguage.Unknown, ScriptLanguageClassifier.Classify(text));
    }

    [Fact]
    public void Japanese_punctuation_does_not_change_the_counts()
    {
        Assert.Equal(ScriptLanguageClassifier.Classify("散開"), ScriptLanguageClassifier.Classify("「散開」！？～"));
        Assert.Equal(ScriptLanguage.En, ScriptLanguageClassifier.Classify("ok、。！？"));
    }

    [Fact]
    public void Classification_never_modifies_the_input()
    {
        const string original = "ｵｹ ＯＫ";
        var copy = new string(original.AsSpan());
        ScriptLanguageClassifier.Classify(original);
        Assert.Equal(original, copy);
    }

    [Fact]
    public void Every_bench_source_is_classified_in_its_direction()
    {
        // All 48 sources, including the 【…】 auto-translate case, classify in their bench direction.
        foreach (var c in BenchFixtures.LoadCases())
        {
            var expected = c.Direction == "jp-en" ? ScriptLanguage.Ja : ScriptLanguage.En;
            Assert.True(expected == ScriptLanguageClassifier.Classify(c.Source), $"{c.Id}: {c.Source}");
        }
    }
}
