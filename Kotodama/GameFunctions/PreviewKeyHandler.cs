using System;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using EchoXIV.Outgoing;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// Answers the translation preview with Enter/Esc read from the game's key state, and hides
    /// those keys from the game while a preview is up (docs/PLAN.md P1.6). Works whichever ImGui
    /// window has focus, which matters with ChatTwo. Runs on the framework thread and never waits.
    /// </summary>
    internal sealed unsafe class PreviewKeyHandler : IDisposable
    {
        private readonly IFramework _framework;
        private readonly IKeyState _keyState;
        private readonly ConfirmationGate _gate;
        private readonly Func<bool> _imGuiTextInputActive;
        private readonly IPluginLog _log;
        private readonly PreviewKeyTracker _tracker = new();

        public PreviewKeyHandler(IFramework framework, IKeyState keyState, ConfirmationGate gate, Func<bool> imGuiTextInputActive, IPluginLog log)
        {
            _framework = framework;
            _keyState = keyState;
            _gate = gate;
            _imGuiTextInputActive = imGuiTextInputActive;
            _log = log;
            _framework.Update += OnUpdate;
        }

        private void OnUpdate(IFramework framework)
        {
            try
            {
                var pending = _gate.Current;
                var enter = _keyState[VirtualKey.RETURN];
                var escape = _keyState[VirtualKey.ESCAPE];
                if (pending == null && !enter && !escape)
                {
                    _tracker.Update(new PreviewKeyInput(null, false, false, false, DateTimeOffset.UtcNow));
                    return;
                }

                var result = _tracker.Update(new PreviewKeyInput(
                    pending?.Id,
                    enter,
                    escape,
                    IsTextInputActive(),
                    DateTimeOffset.UtcNow));

                if (result.SwallowEnter)
                    _keyState[VirtualKey.RETURN] = false;
                if (result.SwallowEscape)
                    _keyState[VirtualKey.ESCAPE] = false;

                if (result.Answer is { } answer)
                    _gate.Resolve(result.Id, answer);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Preview key handling failed.");
            }
        }

        private bool IsTextInputActive()
        {
            if (_imGuiTextInputActive())
                return true;

            var raptureAtk = RaptureAtkModule.Instance();
            return raptureAtk != null && raptureAtk->AtkModule.IsTextInputActive();
        }

        public void Dispose() => _framework.Update -= OnUpdate;
    }
}
