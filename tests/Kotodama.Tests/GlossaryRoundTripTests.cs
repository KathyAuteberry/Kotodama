using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public sealed class GlossaryRoundTripTests
{
    private static string Restore(string text) =>
        text.Replace("[[TEA_ULT]]", "TEA").Replace("[[PF]]", "PF");

    [Theory]
    [InlineData("tea is fun", "[[TEA_ULT]] is fun")]
    [InlineData("i suggest dont do it in pf", "i suggest dont do it in [[PF]]")]
    [InlineData("hello there", "hello there")]
    public void An_untranslated_line_keeps_the_players_spelling(string source, string protectedText) =>
        Assert.Equal(source, GlossaryRoundTrip.Finish(source, protectedText, protectedText, Restore));

    [Fact]
    public void A_translation_gets_its_terms_restored() =>
        Assert.Equal(
            "Shall we do TEA in PF?",
            GlossaryRoundTrip.Finish("teaをpfでやりませんか", "[[TEA_ULT]]を[[PF]]でやりませんか", "Shall we do [[TEA_ULT]] in [[PF]]?", Restore));

    private static string Protect(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, @"\btea\b", "[[TEA_ULT]]", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            @"\bpf\b", "[[PF]]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [Fact]
    public void A_cached_pass_through_serves_the_players_spelling() =>
        Assert.Equal("tea is fun", GlossaryRoundTrip.FromCache("tea is fun", "TEA is fun", Protect));

    [Fact]
    public void A_cached_translation_is_served_as_is() =>
        Assert.Equal("Shall we do TEA in PF?", GlossaryRoundTrip.FromCache("teaをpfでやりませんか", "Shall we do TEA in PF?", Protect));
}
