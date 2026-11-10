using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public class FailoverDecisionTests
{
    private static EngineInstance Engine(TranslationEngine type, bool enabled = true) => new() { Type = type, Enabled = enabled };

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.LocalOnly)]
    public void Local_policies_never_switch(FallbackPolicy policy)
    {
        var local = Engine(TranslationEngine.LocalAI);
        var engines = new[] { local, Engine(TranslationEngine.Google), Engine(TranslationEngine.Papago) };

        Assert.Null(FailoverDecision.ChooseFailoverTarget(policy, engines, local.Id));
    }

    [Theory]
    [InlineData(FallbackPolicy.None)]
    [InlineData(FallbackPolicy.LocalOnly)]
    [InlineData(FallbackPolicy.Any)]
    public void A_disabled_google_instance_is_never_chosen(FallbackPolicy policy)
    {
        var local = Engine(TranslationEngine.LocalAI);
        var disabledGoogle = Engine(TranslationEngine.Google, enabled: false);
        var engines = new[] { local, disabledGoogle };

        Assert.Null(FailoverDecision.ChooseFailoverTarget(policy, engines, local.Id));
        Assert.False(disabledGoogle.Enabled);
    }

    [Fact]
    public void Any_switches_to_an_enabled_google_instance()
    {
        var local = Engine(TranslationEngine.LocalAI);
        var google = Engine(TranslationEngine.Google);

        Assert.Equal(google.Id, FailoverDecision.ChooseFailoverTarget(FallbackPolicy.Any, new[] { local, google }, local.Id));
    }

    [Fact]
    public void Any_does_not_switch_when_google_is_already_primary()
    {
        var google = Engine(TranslationEngine.Google);

        Assert.Null(FailoverDecision.ChooseFailoverTarget(FallbackPolicy.Any, new[] { google }, google.Id));
    }

    [Fact]
    public void Any_does_not_invent_a_google_instance()
    {
        var local = Engine(TranslationEngine.LocalAI);

        Assert.Null(FailoverDecision.ChooseFailoverTarget(FallbackPolicy.Any, new[] { local }, local.Id));
    }

    [Theory]
    [InlineData(FallbackPolicy.None, true, false)]
    [InlineData(FallbackPolicy.LocalOnly, true, false)]
    [InlineData(FallbackPolicy.Any, true, true)]
    [InlineData(FallbackPolicy.Any, false, false)]
    public void Local_ai_recovery_only_runs_under_any(FallbackPolicy policy, bool unavailable, bool expected)
    {
        Assert.Equal(expected, FailoverDecision.ShouldRunLocalAiRecovery(policy, unavailable));
    }
}
