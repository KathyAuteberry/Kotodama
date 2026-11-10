using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public class EngineSelectionTests
{
    private static EngineInstance Engine(TranslationEngine type, bool enabled = true, string? name = null) =>
        new() { Type = type, Enabled = enabled, Name = name ?? type.ToString() };

    /// <summary>The engine list upstream EchoXIV creates on first run.</summary>
    private static List<EngineInstance> UpstreamDefaults() => new()
    {
        Engine(TranslationEngine.Google),
        Engine(TranslationEngine.Papago),
        Engine(TranslationEngine.Yandex),
    };

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.LocalOnly)]
    public void Upstream_default_engines_are_blocked_under_local_policies(FallbackPolicy policy)
    {
        var engines = UpstreamDefaults();

        var result = EngineSelection.Select(engines, engines[0].Id, policy);

        Assert.Empty(result.Engines);
        Assert.Equal(EngineSelectionBlock.CloudPrimaryNotAllowed, result.BlockedReason);
        Assert.Null(result.Secondary);
    }

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.LocalOnly)]
    public void Local_policies_never_select_a_cloud_engine(FallbackPolicy policy)
    {
        var local1 = Engine(TranslationEngine.LocalAI, name: "PLaMo");
        var engines = new List<EngineInstance>
        {
            Engine(TranslationEngine.Google),
            local1,
            Engine(TranslationEngine.Papago),
            Engine(TranslationEngine.LocalAI, name: "Backup"),
            Engine(TranslationEngine.Gemini),
        };

        var result = EngineSelection.Select(engines, local1.Id, policy);

        Assert.NotEmpty(result.Engines);
        Assert.All(result.Engines, e => Assert.True(EngineLocality.IsLocal(e)));
        Assert.Same(local1, result.Primary);
    }

    [Fact]
    public void None_selects_only_the_local_primary()
    {
        var primary = Engine(TranslationEngine.LocalAI, name: "PLaMo");
        var engines = new List<EngineInstance> { primary, Engine(TranslationEngine.LocalAI, name: "Backup") };

        var result = EngineSelection.Select(engines, primary.Id, FallbackPolicy.None);

        Assert.Equal(new[] { primary }, result.Engines);
        Assert.Null(result.Secondary);
        Assert.Null(result.BlockedReason);
    }

    [Fact]
    public void LocalOnly_keeps_list_order_for_local_fallbacks()
    {
        var a = Engine(TranslationEngine.LocalAI, name: "A");
        var b = Engine(TranslationEngine.LocalAI, name: "B");
        var c = Engine(TranslationEngine.LocalAI, name: "C");
        var engines = new List<EngineInstance> { a, Engine(TranslationEngine.Google), b, c };

        var result = EngineSelection.Select(engines, b.Id, FallbackPolicy.LocalOnly);

        Assert.Equal(new[] { b, a, c }, result.Engines);
        Assert.Same(a, result.Secondary);
    }

    [Fact]
    public void Any_keeps_upstream_order_primary_first_then_list_order()
    {
        var engines = UpstreamDefaults();
        var local = Engine(TranslationEngine.LocalAI);
        engines.Add(local);

        var result = EngineSelection.Select(engines, local.Id, FallbackPolicy.Any);

        Assert.Equal(new[] { local, engines[0], engines[1], engines[2] }, result.Engines);
    }

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.LocalOnly)]
    [InlineData(FallbackPolicy.Any)]
    public void Disabled_instances_are_never_selected(FallbackPolicy policy)
    {
        var primary = Engine(TranslationEngine.LocalAI);
        var engines = new List<EngineInstance>
        {
            primary,
            Engine(TranslationEngine.LocalAI, enabled: false),
            Engine(TranslationEngine.Google, enabled: false),
        };

        var result = EngineSelection.Select(engines, primary.Id, policy);

        Assert.Equal(new[] { primary }, result.Engines);
    }

    [Fact]
    public void Disabled_primary_falls_back_to_first_enabled_instance()
    {
        var disabled = Engine(TranslationEngine.LocalAI, enabled: false);
        var enabled = Engine(TranslationEngine.LocalAI);

        var result = EngineSelection.Select(new[] { disabled, enabled }, disabled.Id, FallbackPolicy.None);

        Assert.Same(enabled, result.Primary);
    }

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.Any)]
    public void Nothing_enabled_is_blocked(FallbackPolicy policy)
    {
        var result = EngineSelection.Select(new[] { Engine(TranslationEngine.LocalAI, enabled: false) }, Guid.Empty, policy);

        Assert.Empty(result.Engines);
        Assert.Equal(EngineSelectionBlock.NoEnabledEngine, result.BlockedReason);
    }

    [Fact]
    public void Unknown_policy_value_fails_closed_to_local_only()
    {
        var engines = UpstreamDefaults();

        var result = EngineSelection.Select(engines, engines[0].Id, (FallbackPolicy)42);

        Assert.Empty(result.Engines);
        Assert.Equal(EngineSelectionBlock.CloudPrimaryNotAllowed, result.BlockedReason);
    }

    [Theory]
    [InlineData(FallbackPolicy.None, true)]
    [InlineData(FallbackPolicy.LocalOnly, true)]
    [InlineData(FallbackPolicy.Any, false)]
    public void IsLocalOnly_matches_policy(FallbackPolicy policy, bool expected)
    {
        Assert.Equal(expected, EngineSelection.IsLocalOnly(policy));
    }
}
