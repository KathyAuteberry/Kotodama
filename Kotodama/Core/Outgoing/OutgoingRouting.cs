namespace EchoXIV.Outgoing
{
    /// <summary>How lines typed into the chat box are treated (docs/PLAN.md P1.1).</summary>
    public enum OutgoingMode
    {
        /// <summary>No outgoing translation; <c>/tl</c> is refused.</summary>
        Off = 0,

        /// <summary>Default: typed lines go to the game untouched; only <c>/tl &lt;text&gt;</c> is translated.</summary>
        ExplicitOnly = 1,

        /// <summary>Upstream behaviour: every plain typed line is translated before sending.</summary>
        AutoIntercept = 2,
    }

    /// <summary>What the chat-box hook does with one entered line.</summary>
    public enum OutgoingDecision
    {
        /// <summary>Hand the line to the game immediately, unchanged.</summary>
        Bypass,

        /// <summary>
        /// Send the line unchanged, but through the outgoing queue so it cannot overtake earlier
        /// lines that are still being translated (strict ordering, decision 2).
        /// </summary>
        PassThrough,

        /// <summary>Translate the line, then send it through the outgoing queue.</summary>
        Translate,
    }

    public static class OutgoingRouting
    {
        /// <param name="isExcluded">The line is on the "never translate" list.</param>
        /// <param name="isChannelCommand">The line starts with a chat-channel command such as <c>/p</c>.</param>
        /// <param name="hasPayload">The line contains SeString payloads (auto-translate phrases, item links).</param>
        /// <param name="queueEmpty">No earlier line is still waiting in the outgoing queue.</param>
        public static OutgoingDecision Decide(
            OutgoingMode mode,
            string text,
            bool isExcluded,
            bool isChannelCommand,
            bool hasPayload,
            bool queueEmpty)
        {
            if (string.IsNullOrWhiteSpace(text))
                return OutgoingDecision.Bypass;

            var keepOrder = queueEmpty ? OutgoingDecision.Bypass : OutgoingDecision.PassThrough;

            // Commands go to the game (Dalamud handles /tl etc.). Chat-channel commands are chat
            // lines, so they wait behind pending translations; other commands never wait.
            if (text.StartsWith('/'))
                return isChannelCommand ? keepOrder : OutgoingDecision.Bypass;

            if (mode != OutgoingMode.AutoIntercept)
                return keepOrder;

            // Payload lines are passed through untranslated until payload handling is tested in game.
            if (isExcluded || hasPayload)
                return keepOrder;

            return OutgoingDecision.Translate;
        }
    }
}
