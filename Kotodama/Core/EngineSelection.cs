using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoXIV
{
    /// <summary>Which engines may be tried after (or instead of) the primary instance.</summary>
    public enum FallbackPolicy
    {
        /// <summary>Local-only: the primary instance must be local and nothing else is tried.</summary>
        None = 0,

        /// <summary>The primary instance must be local; other enabled local instances are tried next.</summary>
        LocalOnly = 1,

        /// <summary>Upstream behaviour: every enabled instance, cloud included, in list order.</summary>
        Any = 2,
    }

    /// <summary>
    /// Result of <see cref="EngineSelection.Select"/>. <see cref="Engines"/> is the complete, ordered
    /// list of instances translation may use (primary first). When it is empty,
    /// <see cref="BlockedReason"/> says why.
    /// </summary>
    public sealed record EngineSelectionResult(IReadOnlyList<EngineInstance> Engines, EngineSelectionBlock? BlockedReason)
    {
        public EngineInstance? Primary => Engines.Count > 0 ? Engines[0] : null;

        /// <summary>The engine tried after the primary, or null when there is no fallback.</summary>
        public EngineInstance? Secondary => Engines.Count > 1 ? Engines[1] : null;
    }

    public enum EngineSelectionBlock
    {
        /// <summary>No instance is enabled.</summary>
        NoEnabledEngine,

        /// <summary>The primary instance is a cloud service but the policy only allows local engines.</summary>
        CloudPrimaryNotAllowed,
    }

    /// <summary>
    /// The single choke point that decides which engine instances translation may reach
    /// (docs/PLAN.md P2.2). The router, the fallback/secondary translator and every failover path
    /// must be built only from <see cref="Select"/>'s result, so under <see cref="FallbackPolicy.None"/>
    /// and <see cref="FallbackPolicy.LocalOnly"/> no code path can reach a cloud engine.
    /// </summary>
    public static class EngineSelection
    {
        public static EngineSelectionResult Select(IEnumerable<EngineInstance> instances, Guid primaryId, FallbackPolicy policy)
        {
            var enabled = instances.Where(e => e.Enabled).ToList();
            var primary = enabled.FirstOrDefault(e => e.Id == primaryId) ?? enabled.FirstOrDefault();
            if (primary == null)
                return Blocked(EngineSelectionBlock.NoEnabledEngine);

            var others = enabled.Where(e => e.Id != primary.Id);

            switch (policy)
            {
                case FallbackPolicy.Any:
                    return Allowed(primary, others);

                case FallbackPolicy.LocalOnly:
                    return EngineLocality.IsLocal(primary)
                        ? Allowed(primary, others.Where(EngineLocality.IsLocal))
                        : Blocked(EngineSelectionBlock.CloudPrimaryNotAllowed);

                case FallbackPolicy.None:
                default:
                    // Unknown values fail closed to the strictest policy.
                    return EngineLocality.IsLocal(primary)
                        ? Allowed(primary, Enumerable.Empty<EngineInstance>())
                        : Blocked(EngineSelectionBlock.CloudPrimaryNotAllowed);
            }
        }

        /// <summary>True when the policy never allows a cloud engine.</summary>
        public static bool IsLocalOnly(FallbackPolicy policy) => policy != FallbackPolicy.Any;

        private static EngineSelectionResult Allowed(EngineInstance primary, IEnumerable<EngineInstance> others) =>
            new(new[] { primary }.Concat(others).ToList(), null);

        private static EngineSelectionResult Blocked(EngineSelectionBlock reason) =>
            new(Array.Empty<EngineInstance>(), reason);
    }
}
