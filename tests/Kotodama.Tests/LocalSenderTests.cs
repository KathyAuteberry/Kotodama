using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public sealed class LocalSenderTests
{
    private const string Me = "Kathy Auteberry";

    [Theory]
    [InlineData("Kathy Auteberry")]
    [InlineData("Kathy Auteberry@Tonberry")]
    [InlineData("Kathy Auteberry＠Tonberry")]
    [InlineData("Kathy AuteberryTonberry")]
    [InlineData("Kathy Auteberry")]          // party number glyph
    [InlineData(" Kathy Auteberry@Tonberry ")]
    public void The_local_player_is_recognised(string sender) =>
        Assert.True(LocalSender.Is(sender, Me));

    [Theory]
    [InlineData("Popo Rino@Tonberry")]
    [InlineData("Kathy Auteberryson")]             // longer name starting with ours
    [InlineData("Kathy")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_senders_are_not(string? sender) =>
        Assert.False(LocalSender.Is(sender, Me));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_local_name_matches_nobody(string? localName) =>
        Assert.False(LocalSender.Is("Kathy Auteberry", localName));
}
