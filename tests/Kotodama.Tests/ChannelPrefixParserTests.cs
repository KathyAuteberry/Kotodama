using EchoXIV.Outgoing;
using Xunit;

namespace Kotodama.Tests;

public class ChannelPrefixParserTests
{
    [Fact]
    public void Plain_text_has_no_target_and_keeps_the_body()
    {
        var result = ChannelPrefixParser.Parse("  Please wait, my game crashed.  ");

        Assert.False(result.IsError);
        Assert.Null(result.Target);
        Assert.Equal("Please wait, my game crashed.", result.Body);
    }

    [Theory]
    [InlineData("/p Stack at C, then spread!", ChatChannelKind.Party, 0, "/p", "Stack at C, then spread!")]
    [InlineData("/party hi", ChatChannelKind.Party, 0, "/p", "hi")]
    [InlineData("/fc gn all", ChatChannelKind.FreeCompany, 0, "/fc", "gn all")]
    [InlineData("/a hi", ChatChannelKind.Alliance, 0, "/a", "hi")]
    [InlineData("/s hi", ChatChannelKind.Say, 0, "/s", "hi")]
    [InlineData("/sh hi", ChatChannelKind.Shout, 0, "/sh", "hi")]
    [InlineData("/y hi", ChatChannelKind.Yell, 0, "/y", "hi")]
    [InlineData("/l3 hi", ChatChannelKind.Linkshell, 3, "/l3", "hi")]
    [InlineData("/linkshell8 hi", ChatChannelKind.Linkshell, 8, "/l8", "hi")]
    [InlineData("/cwl1 hi", ChatChannelKind.CrossWorldLinkshell, 1, "/cwl1", "hi")]
    [InlineData("/cwlinkshell2 hi", ChatChannelKind.CrossWorldLinkshell, 2, "/cwl2", "hi")]
    [InlineData("/e test", ChatChannelKind.Echo, 0, "/e", "test")]
    [InlineData("/r thanks", ChatChannelKind.Reply, 0, "/r", "thanks")]
    [InlineData("/beginner hi", ChatChannelKind.NoviceNetwork, 0, "/beginner", "hi")]
    [InlineData("/P hi", ChatChannelKind.Party, 0, "/p", "hi")]
    public void Explicit_channel_prefixes(string input, ChatChannelKind kind, int index, string prefix, string body)
    {
        var result = ChannelPrefixParser.Parse(input);

        Assert.False(result.IsError);
        Assert.Equal(kind, result.Target!.Kind);
        Assert.Equal(index, result.Target.Index);
        Assert.Equal(prefix, result.Target.Prefix);
        Assert.Equal(body, result.Body);
    }

    [Theory]
    [InlineData("／ｐ　軽減お願いします")]
    [InlineData("/p　軽減お願いします")]
    public void Fullwidth_slash_command_and_ideographic_space_are_accepted(string input)
    {
        var result = ChannelPrefixParser.Parse(input);

        Assert.Equal(ChatChannelKind.Party, result.Target!.Kind);
        Assert.Equal("軽減お願いします", result.Body);
    }

    [Theory]
    [InlineData("/t Kath Auteberry@Twintania hi there", "Kath Auteberry@Twintania", "hi there")]
    [InlineData("/tell Kath Auteberry@Twintania hi", "Kath Auteberry@Twintania", "hi")]
    [InlineData("/t \"Kath Auteberry@Twintania\" hi", "Kath Auteberry@Twintania", "hi")]
    [InlineData("/t Kath Auteberry hi", "Kath Auteberry", "hi")]
    [InlineData("/t <t> hi", "<t>", "hi")]
    [InlineData("/t Kath@Twintania hi", "Kath@Twintania", "hi")]
    public void Tell_recipients(string input, string recipient, string body)
    {
        var result = ChannelPrefixParser.Parse(input);

        Assert.False(result.IsError);
        Assert.Equal(ChatChannelKind.Tell, result.Target!.Kind);
        Assert.Equal(recipient, result.Target.TellRecipient);
        Assert.Equal($"/t {recipient}", result.Target.Prefix);
        Assert.Equal(body, result.Body);
    }

    [Theory]
    [InlineData("/t")]
    [InlineData("/t Kath hi")]
    [InlineData("/t \"Kath Auteberry hi")]
    public void Tell_without_a_clear_recipient_is_an_error(string input)
    {
        Assert.Equal(ChannelParseError.MissingTellRecipient, ChannelPrefixParser.Parse(input).Error);
    }

    [Fact]
    public void Tell_with_recipient_but_no_message_is_empty()
    {
        Assert.Equal(ChannelParseError.EmptyMessage, ChannelPrefixParser.Parse("/t Kath Auteberry@Twintania").Error);
    }

    [Theory]
    [InlineData("/em waves")]
    [InlineData("/dance")]
    [InlineData("/xlplugins")]
    public void Unknown_commands_are_rejected_not_translated(string input)
    {
        var result = ChannelPrefixParser.Parse(input);

        Assert.Equal(ChannelParseError.UnsupportedCommand, result.Error);
        Assert.Null(result.Target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/p")]
    [InlineData("/p   ")]
    public void Empty_bodies_are_errors(string input)
    {
        Assert.Equal(ChannelParseError.EmptyMessage, ChannelPrefixParser.Parse(input).Error);
    }

    [Theory]
    [InlineData("/p hi", true)]
    [InlineData("/t Kath Auteberry hi", true)]
    [InlineData("/cwl2 hi", true)]
    [InlineData("/tl hi", false)]
    [InlineData("/dance", false)]
    [InlineData("hello", false)]
    public void IsChannelCommand(string text, bool expected)
    {
        Assert.Equal(expected, ChannelPrefixParser.IsChannelCommand(text));
    }

    [Theory]
    [InlineData(1, "/s")]
    [InlineData(2, "/p")]
    [InlineData(3, "/a")]
    [InlineData(4, "/y")]
    [InlineData(5, "/sh")]
    [InlineData(6, "/fc")]
    [InlineData(7, "/pvpteam")]
    [InlineData(8, "/beginner")]
    [InlineData(9, "/cwl1")]
    [InlineData(16, "/cwl8")]
    [InlineData(19, "/l1")]
    [InlineData(26, "/l8")]
    public void Active_input_channel_maps_to_an_explicit_prefix(int chatType, string prefix)
    {
        Assert.Equal(prefix, ChannelPrefixParser.FromInputChannel(chatType, null, null)!.Prefix);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(18)]
    public void Active_tell_channel_uses_the_tell_target(int chatType)
    {
        Assert.Equal("/t Kath Auteberry@Twintania", ChannelPrefixParser.FromInputChannel(chatType, "Kath Auteberry", "Twintania")!.Prefix);
        Assert.Equal("/t Kath Auteberry", ChannelPrefixParser.FromInputChannel(chatType, "Kath Auteberry", "")!.Prefix);
        Assert.Null(ChannelPrefixParser.FromInputChannel(chatType, "", "Twintania"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(27)]
    [InlineData(99)]
    public void Unknown_input_channels_have_no_target(int chatType)
    {
        Assert.Null(ChannelPrefixParser.FromInputChannel(chatType, null, null));
    }

    [Fact]
    public void ToCommand_joins_prefix_and_body()
    {
        Assert.Equal("/l2 hello", new ChannelTarget(ChatChannelKind.Linkshell, 2).ToCommand("hello"));
    }
}
