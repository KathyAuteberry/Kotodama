using System.Collections.Generic;

namespace EchoXIV
{
    /// <summary>
    /// First-run engine configuration (docs/PLAN.md P2.4). Kotodama ships publicly, so nothing is
    /// pre-filled that would send chat anywhere: one Local AI instance with no endpoint, and no
    /// cloud engines. Users can still add cloud engines themselves.
    /// </summary>
    public static class DefaultEngines
    {
        public static List<EngineInstance> Create() => new()
        {
            new EngineInstance { Type = TranslationEngine.LocalAI, Name = "Local AI", Enabled = true },
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
