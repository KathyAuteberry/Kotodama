using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoXIV
{
    /// <summary>
    /// Decides what the persistent "switch engines after a failure" path may do
    /// (upstream SwitchToGoogleFailover). Under a local-only policy it never does anything,
    /// and it never re-enables an instance the user disabled (docs/PLAN.md P2.3).
    /// </summary>
    public static class FailoverDecision
    {
        /// <summary>
        /// Returns the instance to make primary after the current primary failed, or null to keep
        /// the configuration unchanged. Only <see cref="FallbackPolicy.Any"/> may switch, and only
        /// to an existing, enabled Google instance (upstream's target).
        /// </summary>
        public static Guid? ChooseFailoverTarget(FallbackPolicy policy, IEnumerable<EngineInstance> engines, Guid currentPrimaryId)
        {
            if (policy != FallbackPolicy.Any)
                return null;

            var list = engines.ToList();
            var current = list.FirstOrDefault(e => e.Id == currentPrimaryId);
            if (current?.Type == TranslationEngine.Google)
                return null;

            return list.FirstOrDefault(e => e.Type == TranslationEngine.Google && e.Enabled)?.Id;
        }

        /// <summary>
        /// Whether the periodic "is Local AI back?" check may run and restore Local AI as primary.
        /// It only exists to undo an automatic switch, which only <see cref="FallbackPolicy.Any"/> makes.
        /// </summary>
        public static bool ShouldRunLocalAiRecovery(FallbackPolicy policy, bool localAiUnavailable) =>
            policy == FallbackPolicy.Any && localAiUnavailable;
    }
}
