using System;

namespace EchoXIV.Outgoing
{
    /// <summary>Key state for one game frame while deciding a preview.</summary>
    public readonly record struct PreviewKeyInput(
        long? PendingId,
        bool EnterDown,
        bool EscapeDown,
        bool TextInputActive,
        DateTimeOffset Now);

    /// <summary>
    /// What to do this frame: answer the pending preview (true send, false cancel) and which keys
    /// to hide from the game so it doesn't also open the chat input or the system menu.
    /// </summary>
    public readonly record struct PreviewKeyResult(long Id, bool? Answer, bool SwallowEnter, bool SwallowEscape);

    /// <summary>
    /// Reads Enter/Esc from the game's key state instead of ImGui focus (docs/PLAN.md P1.6), so a
    /// preview works with chat replacements such as ChatTwo that take ImGui focus back. Rules:
    /// <list type="bullet">
    /// <item>A key answers only on a fresh press after the preview appeared, and not within the grace period.</item>
    /// <item>While a text input (game chat or an ImGui text box) is active, keys belong to it: no answer, nothing hidden.</item>
    /// <item>While a preview is pending, Enter/Esc are hidden from the game; a key that answered stays hidden until released.</item>
    /// </list>
    /// Pure and single-threaded (called once per framework tick).
    /// </summary>
    public sealed class PreviewKeyTracker
    {
        public static readonly TimeSpan DefaultGrace = TimeSpan.FromMilliseconds(250);

        private readonly TimeSpan _grace;
        private long? _id;
        private DateTimeOffset _shownAt;
        private bool _enterArmed;
        private bool _escapeArmed;
        private bool _holdEnter;
        private bool _holdEscape;

        public PreviewKeyTracker(TimeSpan? grace = null) => _grace = grace ?? DefaultGrace;

        public PreviewKeyResult Update(PreviewKeyInput input)
        {
            // Keys that answered a preview stay hidden until released, whatever happens next.
            if (!input.EnterDown) _holdEnter = false;
            if (!input.EscapeDown) _holdEscape = false;
            var swallowEnter = _holdEnter;
            var swallowEscape = _holdEscape;

            if (input.PendingId != _id)
            {
                _id = input.PendingId;
                _shownAt = input.Now;
                _enterArmed = false;
                _escapeArmed = false;
            }

            if (_id is not { } id)
                return new PreviewKeyResult(0, null, swallowEnter, swallowEscape);

            if (input.TextInputActive)
            {
                // The user is typing; a later Enter there must not confirm the preview.
                _enterArmed = false;
                _escapeArmed = false;
                return new PreviewKeyResult(id, null, swallowEnter, swallowEscape);
            }

            var ready = input.Now - _shownAt >= _grace;
            bool? answer = null;

            // A press inside the grace period is used up: it answers nothing, even once the grace ends.
            if (!input.EnterDown)
                _enterArmed = true;
            else if (_enterArmed && ready)
                answer = true;
            else
                _enterArmed = false;

            if (!input.EscapeDown)
                _escapeArmed = true;
            else if (answer == null && _escapeArmed && ready)
                answer = false;
            else
                _escapeArmed = false;

            if (answer != null)
            {
                _enterArmed = false;
                _escapeArmed = false;
                if (answer == true) _holdEnter = true;
                else _holdEscape = true;
            }

            return new PreviewKeyResult(id, answer, swallowEnter || input.EnterDown, swallowEscape || input.EscapeDown);
        }
    }
}
