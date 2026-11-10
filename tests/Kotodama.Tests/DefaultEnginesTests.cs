using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public class DefaultEnginesTests
{
    [Fact]
    public void Fresh_config_contains_no_cloud_engines()
    {
        var engines = DefaultEngines.Create();

        Assert.NotEmpty(engines);
        Assert.All(engines, e => Assert.True(EngineLocality.IsLocal(e)));
    }

    [Fact]
    public void Fresh_config_has_no_preconfigured_server()
    {
        var engines = DefaultEngines.Create();

        Assert.All(engines, e => Assert.True(string.IsNullOrEmpty(e.Endpoint)));
    }

    [Fact]
    public void Each_call_returns_new_instances_with_unique_ids()
    {
        var first = DefaultEngines.Create();
        var second = DefaultEngines.Create();

        Assert.NotSame(first, second);
        Assert.DoesNotContain(first, e => second.Any(o => o.Id == e.Id));
    }

    [Fact]
    public void Fresh_config_is_blocked_until_the_server_url_is_entered()
    {
        var engines = DefaultEngines.Create();
        var endpointEntered = false;
        bool IsConfigured(EngineInstance e) => endpointEntered;

        var before = EngineSelection.Select(engines, Guid.Empty, FallbackPolicy.None, IsConfigured);
        endpointEntered = true;
        var after = EngineSelection.Select(engines, Guid.Empty, FallbackPolicy.None, IsConfigured);

        Assert.Equal(EngineSelectionBlock.PrimaryNotConfigured, before.BlockedReason);
        Assert.Empty(before.Engines);
        Assert.Same(engines[0], after.Primary);
    }

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.LocalOnly)]
    public void Cloud_engines_are_never_auto_enabled_under_local_policies(FallbackPolicy policy)
    {
        foreach (var type in Enum.GetValues<TranslationEngine>().Where(t => !EngineLocality.IsLocal(t)))
            Assert.False(DefaultEngines.MayAutoEnable(new EngineInstance { Type = type }, policy), type.ToString());
    }

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.LocalOnly)]
    [InlineData(FallbackPolicy.Any)]
    public void Local_engines_may_be_auto_enabled(FallbackPolicy policy)
    {
        Assert.True(DefaultEngines.MayAutoEnable(new EngineInstance { Type = TranslationEngine.LocalAI }, policy));
    }

    [Fact]
    public void Any_keeps_upstream_auto_enable_behaviour()
    {
        Assert.True(DefaultEngines.MayAutoEnable(new EngineInstance { Type = TranslationEngine.Google }, FallbackPolicy.Any));
    }
}
