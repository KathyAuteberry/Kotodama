using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EchoXIV.Outgoing;
using EchoXIV.Properties;

namespace EchoXIV.UI;

/// <summary>
/// Shows the translation at the head of the outgoing queue and waits for Enter (send) or Esc
/// (cancel) (docs/PLAN.md P1.6). Drawn only while <see cref="ConfirmationGate.Current"/> is set.
/// Everything here runs in the draw loop and never waits.
/// </summary>
public sealed class PreviewWindow : Window
{
    // Ignore keys briefly after a preview appears, so a key held from typing doesn't answer it.
    private static readonly TimeSpan KeyGrace = PreviewKeyTracker.DefaultGrace;

    private readonly ConfirmationGate _gate;
    private readonly Func<int> _pendingCount;
    private readonly Func<bool> _chatInputIdle;
    private readonly Func<bool> _vanillaTyping;
    private PendingConfirmation? _shown;
    private DateTimeOffset _shownAt;
    private bool _grabFocus;
    private bool _chatIdleLastFrame;
    private bool _typingLastFrame;
    private bool _typingBefore;
    private bool _inputActiveLastFrame;
    private string _keyBuffer = string.Empty;

    /// <param name="chatInputIdle">
    /// True when the focused text input is a chat input with nothing typed (ChatTwo keeps its input
    /// focused after sending). Enter/Esc then answer the preview instead of going to that input.
    /// </param>
    /// <param name="vanillaTyping">True while the game's own chat input is open.</param>
    public PreviewWindow(ConfirmationGate gate, Func<int> pendingCount, Func<bool>? chatInputIdle = null, Func<bool>? vanillaTyping = null)
        : base("Kotodama###KotodamaPreview")
    {
        _gate = gate;
        _pendingCount = pendingCount;
        _chatInputIdle = chatInputIdle ?? (() => false);
        _vanillaTyping = vanillaTyping ?? (() => false);
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions()
    {
        // Called every frame, preview or not: track whether the user is typing so a decision in
        // this frame uses the state from before this frame's key press (an Enter can open the
        // game's chat input or close ChatTwo's in the same frame).
        TrackTyping();

        var current = _gate.Current;
        if (current == null)
        {
            _shown = null;
            return false;
        }

        if (_shown?.Id != current.Id)
        {
            _shown = current;
            _shownAt = DateTimeOffset.UtcNow;
            _grabFocus = true;
        }

        return true;
    }

    public override void PreDraw()
    {
        WindowName = $"{Text("Preview_Title", "Send this translation?")}###KotodamaPreview";
        // Never take focus from someone typing (game chat, ChatTwo with text). Retried every frame
        // until it works, e.g. once the user has sent the line they were typing.
        if (_grabFocus && !_typingBefore)
            ImGui.SetNextWindowFocus();

        ImGui.SetNextWindowSizeConstraints(new Vector2(320, 0), new Vector2(600, float.MaxValue));
    }

    public override void Draw()
    {
        var pending = _shown;
        if (pending == null)
            return;

        var send = pending.Send;
        var translation = send.Translation;
        var grey = new Vector4(0.6f, 0.6f, 0.6f, 1f);

        ImGui.TextColored(grey, $"{Text("Preview_Channel", "Channel")}: " +
            (string.IsNullOrEmpty(send.Item.Prefix) ? Text("Preview_ActiveChannel", "line as typed") : send.Item.Prefix));

        ImGui.TextColored(grey, Text("Preview_Original", "You wrote"));
        ImGui.PushTextWrapPos(560);
        ImGui.TextUnformatted(send.Item.Text);
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.TextColored(grey, Text("Preview_Translation", "Translation"));
        ImGui.PushTextWrapPos(560);
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.6f, 0.9f, 1f, 1f)))
            ImGui.TextUnformatted(translation?.TextToSend ?? send.Line ?? string.Empty);
        if (translation?.RomanizedText is { Length: > 0 } romanized && romanized != translation.TextToSend)
            ImGui.TextColored(grey, romanized);
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        var now = DateTimeOffset.UtcNow;
        var remaining = pending.Deadline - now;
        var total = pending.Deadline - _shownAt;
        var fraction = total.TotalSeconds > 0 ? (float)Math.Clamp(remaining.TotalSeconds / total.TotalSeconds, 0, 1) : 0f;
        ImGui.ProgressBar(fraction, new Vector2(-1, 4), string.Empty);

        var answer = (bool?)null;
        if (ImGui.Button(Text("Preview_Send", "Send (Enter)")))
            answer = true;
        ImGui.SameLine();
        if (ImGui.Button(Text("Preview_Cancel", "Cancel (Esc)")))
            answer = false;

        var waiting = _pendingCount() - 1;
        if (waiting > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(grey, string.Format(Text("Preview_Waiting", "{0} more waiting"), waiting));
        }

        // Holding keyboard focus in an ImGui text field is the only way to keep Enter/Esc from the
        // game: Dalamud's Win32InputHandler swallows WM_KEYDOWN (returns 0) only while
        // io.WantTextInput is true. Without that the game opens its chat input on Enter, and ChatTwo,
        // which hooks the game's chat-open (ChatLogRefresh), focuses its own input. Anything typed
        // here is ignored.
        ImGui.SetNextItemWidth(-1);
        if (_grabFocus && !_typingBefore)
        {
            ImGui.SetKeyboardFocusHere();
            _grabFocus = false;
        }

        _keyBuffer = string.Empty;
        ImGui.InputTextWithHint("##KotodamaPreviewKeys", Text("Preview_KeysHint", "Enter: send · Esc: cancel"), ref _keyBuffer, 16);
        var inputActive = ImGui.IsItemActive();
        var ready = now - _shownAt >= KeyGrace;
        if (answer == null && ImGui.IsItemDeactivated())
        {
            if (ImGui.IsKeyDown(ImGuiKey.Enter) || ImGui.IsKeyDown(ImGuiKey.KeypadEnter))
                answer = ready ? true : null;
            else if (ImGui.IsKeyDown(ImGuiKey.Escape))
                answer = false;
        }
        _inputActiveLastFrame = inputActive;

        // Fallback when we don't hold focus but another ImGui text input does (an empty ChatTwo
        // input): Dalamud then routes keys to ImGui, so Enter/Esc are visible here and answer unless
        // the user was typing in the frame before the press. With no ImGui text input at all,
        // ImGui never receives Enter (Dalamud passes it straight to the game), so only the
        // game-level PreviewKeyHandler can see it, too late to stop the game reacting.
        if (answer == null && !inputActive && ready && (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || !_typingBefore))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false))
                answer = true;
            else if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                answer = false;
        }

        if (answer != null)
            _gate.Resolve(pending.Id, answer.Value);
    }

    /// <summary>
    /// Typing means: the game's chat input is open, or an ImGui text box other than ours is active
    /// and it isn't an empty ChatTwo input (ChatTwo keeps its input focused after sending).
    /// </summary>
    private void TrackTyping()
    {
        var otherTextInput = ImGui.GetIO().WantTextInput && !_inputActiveLastFrame;
        var idleNow = otherTextInput && _chatInputIdle();
        var typingNow = _vanillaTyping() || (otherTextInput && !(idleNow || _chatIdleLastFrame));
        _chatIdleLastFrame = idleNow;
        _typingBefore = _typingLastFrame;
        _typingLastFrame = typingNow;
        if (_gate.Current == null)
            _inputActiveLastFrame = false;
    }

    private static string Text(string key, string fallback) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
}
