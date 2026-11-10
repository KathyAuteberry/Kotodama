using EchoXIV.Plamo;
using EchoXIV.Profiles;
using Xunit;

namespace Kotodama.Tests;

public class TokenGuardTests
{
    [Fact]
    public void Kath_regression_name_becomes_an_identity_term()
    {
        var terms = TokenGuard.IdentityTerms("You can just call me Kath.", new[] { "Kath", "Kath Auteberry" });

        Assert.Equal(new[] { new GlossaryTerm("Kath", "Kath") }, terms);
    }

    [Fact]
    public void Full_name_wins_over_first_name()
    {
        var terms = TokenGuard.IdentityTerms("Kath Auteberry is MT", new[] { "Kath", "Kath Auteberry" });

        Assert.Equal(new[] { new GlossaryTerm("Kath Auteberry", "Kath Auteberry") }, terms);
    }

    [Theory]
    [InlineData("Kathy is here")]
    [InlineData("McKath")]
    [InlineData("nothing")]
    public void Names_match_whole_words_only(string text)
    {
        Assert.Empty(TokenGuard.IdentityTerms(text, new[] { "Kath" }));
    }

    [Fact]
    public void Names_match_next_to_japanese_text()
    {
        Assert.Contains(new GlossaryTerm("Kath", "Kath"), TokenGuard.IdentityTerms("Kathさんお願いします", new[] { "Kath" }));
    }

    [Fact]
    public void Surface_casing_is_kept_and_single_letters_are_ignored()
    {
        Assert.Equal(new[] { new GlossaryTerm("kath", "kath") }, TokenGuard.IdentityTerms("hi kath", new[] { "Kath", "A" }));
    }

    [Theory]
    [InlineData("That was close! ;w;", ";w;")]
    [InlineData("塔踏み忘れましたすみません；；", "；；")]
    [InlineData("gg o/", "o/")]
    public void Emoticons_become_identity_terms(string text, string emoticon)
    {
        Assert.Contains(new GlossaryTerm(emoticon, emoticon), TokenGuard.IdentityTerms(text, Array.Empty<string>()));
    }

    [Fact]
    public void Macro_tags_are_found_in_order()
    {
        Assert.Equal(new[] { "<se.1>", "<t>", "<me>", "<1>", "<wait.3>" },
            TokenGuard.MacroTags("Stack <se.1> on <t>, then <me> <1> <wait.3>"));
    }

    [Theory]
    [InlineData("a < b > c")]
    [InlineData("<3")]
    [InlineData("<<>>")]
    public void Non_tags_are_not_macro_tags(string text)
    {
        Assert.Empty(TokenGuard.MacroTags(text));
    }

    [Fact]
    public void Macro_tags_are_identity_terms()
    {
        Assert.Contains(new GlossaryTerm("<se.1>", "<se.1>"), TokenGuard.IdentityTerms("Spread <se.1>", Array.Empty<string>()));
    }

    [Theory]
    [InlineData("Stack on <t> <se.1>", "<t>で頭割り <se.1>", null)]
    [InlineData("Stack on <t> <se.1>", "<t>で頭割り", "<se.1>")]
    [InlineData("<se.1> go <se.1>", "<se.1> 行って", "<se.1>")]
    [InlineData("<SE.1> go", "<se.1> 行って", null)]
    [InlineData("no tags", "タグなし", null)]
    public void Dropped_macro_tags_are_detected(string source, string output, string? expected)
    {
        Assert.Equal(expected, TokenGuard.FindDroppedTag(source, output));
    }
}
