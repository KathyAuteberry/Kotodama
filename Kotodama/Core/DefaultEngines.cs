using System.Collections.Generic;

namespace EchoXIV
{
    /// <summary>
    /// First-run engine configuration (docs/PLAN.md P2.4, P3.4). Kotodama ships publicly, so nothing
    /// is pre-filled that would send chat anywhere: one PLaMo instance with no server URL, and no
    /// cloud engines. Users can still add cloud engines themselves.
    /// </summary>
    public static class DefaultEngines
    {
        public static List<EngineInstance> Create() => new()
        {
            NewPlamoInstance(),
        };

        /// <summary>
        /// A new PLaMo instance with the benchmarked settings and no server URL
        /// (decision 3: nothing is sent until the user enters one).
        /// </summary>
        public static EngineInstance NewPlamoInstance(string name = "PLaMo", bool enabled = true) => new()
        {
            Type = TranslationEngine.Plamo,
            Name = name,
            Enabled = enabled,
            Endpoint = string.Empty,
            Model = Plamo.PlamoSettings.DefaultModel,
            Temperature = 0f,
            MaxTokens = Plamo.PlamoSettings.DefaultNumPredict,
            KeepAlive = Plamo.PlamoSettings.DefaultKeepAlive,
            TimeoutSeconds = Plamo.PlamoSettings.DefaultTimeoutSeconds,
        };

        /// <summary>
        /// Whether config normalization may enable <paramref name="instance"/> on its own to repair
        /// a configuration with no enabled engine. A cloud engine is never auto-enabled under a
        /// local-only policy.
        /// </summary>
        public static bool MayAutoEnable(EngineInstance instance, FallbackPolicy policy) =>
            !EngineSelection.IsLocalOnly(policy) || EngineLocality.IsLocal(instance);
    }
}
