using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public sealed class PartyFinderDescriptionTests
{
    private static PartyFinderPiece T(string text) => new(PartyFinderPieceKind.Text, text);
    private static readonly PartyFinderPiece Begin = new(PartyFinderPieceKind.PhraseBegin);
    private static readonly PartyFinderPiece End = new(PartyFinderPieceKind.PhraseEnd);
    private static readonly PartyFinderPiece NewLine = new(PartyFinderPieceKind.NewLine);

    [Fact]
    public void Text_between_the_auto_translate_icons_is_a_phrase()
    {
        var text = PartyFinderDescription.Parse(new[]
        {
            T("消化 "), Begin, T("Light Party"), End, T(" 練習中です"),
        });

        Assert.Equal(new[] { "Light Party" }, text.Phrases);
        Assert.Equal("消化 練習中です", text.Translatable);
        Assert.True(PartyFinderDescription.IsJapanese(text));
    }

    [Fact]
    public void Unresolved_auto_translate_payloads_are_phrases_too()
    {
        var text = PartyFinderDescription.Parse(new[]
        {
            new PartyFinderPiece(PartyFinderPieceKind.Phrase, " Duty Complete "), T("固定募集"),
        });

        Assert.Equal(new[] { "Duty Complete" }, text.Phrases);
        Assert.Equal("固定募集", text.Translatable);
    }

    [Fact]
    public void Glyphs_already_in_the_text_split_the_same_way()
    {
        var text = PartyFinderDescription.Parse(new[] { T("初見です Practice よろしく") });

        Assert.Equal(new[] { "Practice" }, text.Phrases);
        Assert.Equal("初見です よろしく", text.Translatable);
    }

    [Fact]
    public void Line_breaks_become_spaces()
    {
        var text = PartyFinderDescription.Parse(new[] { T("散開"), NewLine, T("頭割り") });
        Assert.Equal("散開 頭割り", text.Translatable);
    }

    [Fact]
    public void Begin_icon_without_end_keeps_the_text()
    {
        var text = PartyFinderDescription.Parse(new[] { T("募集 "), Begin, T("途中") });

        Assert.Empty(text.Phrases);
        Assert.Equal("募集 途中", text.Translatable);
    }

    [Theory]
    [InlineData("Clear party, know the fight please")]
    [InlineData("o7")]
    [InlineData("")]
    public void Non_japanese_descriptions_are_not_translated(string description) =>
        Assert.False(PartyFinderDescription.IsJapanese(PartyFinderDescription.Parse(new[] { T(description) })));

    [Fact]
    public void Phrase_only_descriptions_are_not_japanese() =>
        Assert.False(PartyFinderDescription.IsJapanese(PartyFinderDescription.Parse(new[] { Begin, T("Practice"), End })));

    [Theory]
    [InlineData("Duty", "Kath Auteberry", "[KT] PF (Duty, Kath Auteberry): Clear party")]
    [InlineData(null, "Kath Auteberry", "[KT] PF (Kath Auteberry): Clear party")]
    [InlineData(" ", null, "[KT] PF: Clear party")]
    public void Chat_line_names_duty_and_recruiter(string? duty, string? recruiter, string expected) =>
        Assert.Equal(expected, PartyFinderDescription.FormatLine(" Clear party ", duty, recruiter));
}
