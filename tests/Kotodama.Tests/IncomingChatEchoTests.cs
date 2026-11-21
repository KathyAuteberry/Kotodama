using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public sealed class IncomingChatEchoTests
{
    [Fact]
    public void Translation_is_prefixed() =>
        Assert.Equal("[KT] Stack in the middle", IncomingChatEcho.Format("真ん中で頭割り", " Stack in the middle ", failed: false, outgoing: false));

    [Theory]
    [InlineData("hello", "hello", false, false)]   // same-language line, left alone
    [InlineData("散開", "", false, false)]          // nothing translated
    [InlineData("散開", "   ", false, false)]
    [InlineData("散開", null, false, false)]
    [InlineData("散開", "散開", true, false)]       // failed: original shown, not echoed
    [InlineData("hi", "やあ", false, true)]         // our own outgoing line
    public void Nothing_is_printed_when_there_is_no_new_text(string original, string? translated, bool failed, bool outgoing) =>
        Assert.Null(IncomingChatEcho.Format(original, translated, failed, outgoing));

    [Fact]
    public void Auto_translate_only_lines_are_not_echoed()
    {
        // Lines made only of auto-translate phrases compose to the phrases themselves.
        Assert.Null(IncomingChatEcho.Format("【よろしくお願いします！】", "【よろしくお願いします！】", false, false));
    }

    [Fact]
    public void An_untranslated_line_with_phrases_is_not_echoed()
    {
        // "brb 【Bio】": only "brb" went to the engine and came back as is; the composed
        // "【Bio】 brb" differs from the original but isn't a translation.
        Assert.True(IncomingChatEcho.IsPassThrough("brb", "brb"));
        Assert.Null(IncomingChatEcho.Format("brb \uE040 Bio \uE041", "【Bio】 brb", false, false, untranslated: true));
    }

    [Theory]
    [InlineData("brb", " brb ", true)]
    [InlineData("よろしくお願いします", "Looking forward to it.", false)]
    [InlineData("brb", null, false)]
    public void Pass_through_means_the_text_came_back_unchanged(string source, string? translation, bool expected) =>
        Assert.Equal(expected, IncomingChatEcho.IsPassThrough(source, translation));

    [Fact]
    public void A_real_translation_with_phrases_is_echoed() =>
        Assert.Equal("[KT] 【Let's do it!】 Looking forward to it.",
            IncomingChatEcho.Format("よろしくお願いします\uE040 Let's do it! \uE041", "【Let's do it!】 Looking forward to it.", false, false, untranslated: false));

    [Fact]
    public void Registry_skips_each_printed_line_once()
    {
        var registry = new OwnPrintRegistry();
        registry.Register("[KT] hi");
        registry.Register("[KT] hi");

        Assert.False(registry.Take("[KT] other"));
        Assert.True(registry.Take("[KT] hi"));
        Assert.True(registry.Take("[KT] hi"));
        Assert.False(registry.Take("[KT] hi"));
    }

    [Fact]
    public void Registry_entries_expire()
    {
        var now = DateTimeOffset.UnixEpoch;
        var registry = new OwnPrintRegistry(TimeSpan.FromSeconds(30), () => now);
        registry.Register("[KT] hi");

        now += TimeSpan.FromSeconds(31);
        Assert.False(registry.Take("[KT] hi"));
    }
}
