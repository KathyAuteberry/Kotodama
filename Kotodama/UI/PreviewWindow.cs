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
    private PendingConfirmation? _shown;
    private DateTimeOffset _shownAt;
    private bool _focusNextFrame;

    /// <param name="chatInputIdle">
    /// True when the focused text input is a chat input with nothing typed (ChatTwo keeps its input
    /// focused after sending). Enter/Esc then answer the preview instead of going to that input.
    /// </param>
    public PreviewWindow(ConfirmationGate gate, Func<int> pendingCount, Func<bool>? chatInputIdle = null)
        : base("Kotodama###KotodamaPreview")
    {
        _gate = gate;
        _pendingCount = pendingCount;
        _chatInputIdle = chatInputIdle ?? (() => false);
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions()
    {
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
            _focusNextFrame = true;
        }

        return true;
    }

    public override void PreDraw()
    {
        WindowName = $"{Text("Preview_Title", "Send this translation?")}###KotodamaPreview";
        if (_focusNextFrame)
        {
            // Never take focus from someone typing (game chat, ChatTwo). Enter/Esc also work
            // without focus, through GameFunctions.PreviewKeyHandler.
            if (!ImGui.GetIO().WantTextInput)
                ImGui.SetNextWindowFocus();
            _focusNextFrame = false;
        }

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

        // ImGui key state is global, so this works whichever window has focus: either ours, or an
        // empty chat input (ChatTwo) that would otherwise swallow Enter. While something is typed
        // there, the keys stay with it.
        var keysAreOurs = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            || (ImGui.GetIO().WantTextInput && _chatInputIdle());
        if (answer == null && keysAreOurs && now - _shownAt >= KeyGrace)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false))
                answer = true;
            else if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                answer = false;
        }

        if (answer != null)
            _gate.Resolve(pending.Id, answer.Value);
    }

    private static string Text(string key, string fallback) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
}
