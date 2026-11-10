using EchoXIV.Plamo;
using EchoXIV.Profiles;
using Kotodama.Tests.Fixtures;
using Xunit;

namespace Kotodama.Tests;

public class GlossaryMatcherTests
{
    private static readonly IReadOnlyList<ProfileGlossaryEntry> Raid =
        ProfileLoader.Load(Path.Combine(BenchFixtures.RepoRoot, "Kotodama", "Profiles", "Defaults", "Raid")).Glossary;

    private static IReadOnlyList<(string Source, string Target)> Match(string text, TranslationDirection direction) =>
        GlossaryMatcher.Match(text, direction, Raid).Select(t => (t.Source, t.Target)).ToList();

    private static ProfileGlossaryEntry Entry(string ja, string en, GlossaryScope scope = GlossaryScope.Both) =>
        new(ja, en.Split('|'), null, scope, 1);

    [Fact]
    public void Food_maps_to_gohan_for_en_ja()
    {
        Assert.Contains(("food", "ご飯"), Match("Please make sure your food hasn't expired.", TranslationDirection.EnToJa));
    }

    [Fact]
    public void Stack_and_spread_map_for_en_ja()
    {
        var terms = Match("I thought it was a stack but it was a spread.", TranslationDirection.EnToJa);
        Assert.Equal(new[] { ("stack", "頭割り"), ("spread", "散開") }, terms);
    }

    [Fact]
    public void Role_marker_and_tower_match_with_boundaries()
    {
        var terms = Match("Does D1 take the far tower?", TranslationDirection.EnToJa);
        Assert.Equal(new[] { ("D1", "D1"), ("tower", "塔") }, terms);
    }

    [Fact]
    public void Japanese_terms_map_to_their_first_english_alias()
    {
        Assert.Equal(new[] { ("MT", "MT"), ("無敵", "invuln") }, Match("MT無敵受けで", TranslationDirection.JaToEn));
        Assert.Contains(("軽減", "mitigation"), Match("軽減お願いします", TranslationDirection.JaToEn));
    }

    [Fact]
    public void Longest_match_wins_and_overlaps_are_dropped()
    {
        var terms = Match("時計回りじゃなくて反時計回りです", TranslationDirection.JaToEn);
        Assert.Equal(new[] { ("時計回り", "clockwise"), ("反時計回り", "counterclockwise") }, terms);
    }

    [Fact]
    public void Multi_word_aliases_beat_their_parts()
    {
        Assert.Equal(new[] { ("pair stack", "ペア頭割り") }, Match("No partner for the pair stack", TranslationDirection.EnToJa).Where(t => t.Source.Contains("stack")));
        Assert.Contains(("Dark Knight", "暗黒騎士"), Match("I'm a Dark Knight main", TranslationDirection.EnToJa));
        Assert.DoesNotContain(Match("I'm a Dark Knight main", TranslationDirection.EnToJa), t => t.Target == "闇");
    }

    [Theory]
    [InlineData("stacks", "stacks")]
    [InlineData("stacking", "stacking")]
    [InlineData("Stack", "Stack")]
    [InlineData("baiting", "baiting")]
    [InlineData("raised", "raised")]
    [InlineData("raising", "raising")]
    public void Simple_inflections_match(string word, string expectedSource)
    {
        Assert.Contains(Match($"we {word} here", TranslationDirection.EnToJa), t => t.Source == expectedSource);
    }

    [Theory]
    [InlineData("stackable")]
    [InlineData("restack")]
    [InlineData("lightning")]
    [InlineData("D10")]
    [InlineData("AD1")]
    public void No_matches_inside_other_words(string word)
    {
        var terms = Match($"x {word} y", TranslationDirection.EnToJa);
        Assert.Empty(terms);
    }

    [Fact]
    public void Ascii_terms_need_boundaries_in_japanese_text_too()
    {
        Assert.DoesNotContain(Match("D10の人", TranslationDirection.JaToEn), t => t.Source == "D1");
        Assert.Contains(Match("D1とD2逆です", TranslationDirection.JaToEn), t => t.Source == "D1");
    }

    [Fact]
    public void Direction_scope_is_respected()
    {
        // ナイト→PLD is ja-en only; Paladin→ナイト is en-ja only.
        Assert.Contains(("ナイト", "PLD"), Match("ナイトさんST", TranslationDirection.JaToEn));
        Assert.DoesNotContain(Match("PLD can ST", TranslationDirection.EnToJa), t => t.Target == "ナイト");
        Assert.Contains(("Paladin", "ナイト"), Match("Paladin can ST", TranslationDirection.EnToJa));
    }

    [Fact]
    public void Duplicate_terms_are_sent_once()
    {
        Assert.Single(Match("stack, then stack again", TranslationDirection.EnToJa), t => t.Target == "頭割り");
    }

    [Fact]
    public void Cap_limits_the_number_of_terms()
    {
        var entries = Enumerable.Range(0, 40).Select(i => Entry($"語{i:00}", $"w{i:00}")).ToList();
        var text = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"w{i:00}"));

        Assert.Equal(24, GlossaryMatcher.Match(text, TranslationDirection.EnToJa, entries).Count);
        Assert.Equal(5, GlossaryMatcher.Match(text, TranslationDirection.EnToJa, entries, cap: 5).Count);
    }

    [Fact]
    public void Nothing_matches_in_unrelated_text()
    {
        Assert.Empty(Match("You can just call me Kath.", TranslationDirection.EnToJa));
    }
}
