using System;
using System.Text;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using EchoXIV.Outgoing;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// Hooks <c>UIModule.ProcessChatBoxEntry</c> to decide, per entered line, whether it goes to the
    /// game immediately, waits in the outgoing queue unchanged (strict ordering), or is translated
    /// first (AutoIntercept). The detour only reads the line and captures the channel on the
    /// framework thread; it never stores hooked arguments for later replay. Queued lines are sent
    /// by <see cref="ChatSender"/> through a fresh call with an explicit channel prefix.
    /// </summary>
    internal unsafe class ChatBoxHook : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly Func<OutgoingQueue?> _queue;
        private readonly IncomingMessageHandler _incomingMessageHandler;
        private readonly IPluginLog _pluginLog;
        private readonly IChatGui _chatGui;
        private readonly IGameInteropProvider _gameInteropProvider;

        private Hook<UIModule.Delegates.ProcessChatBoxEntry>? _processChatBoxHook;

        public ChatBoxHook(
            Configuration configuration,
            Func<OutgoingQueue?> queue,
            IncomingMessageHandler incomingMessageHandler,
            IPluginLog pluginLog,
            IChatGui chatGui,
            IGameInteropProvider gameInteropProvider)
        {
            _configuration = configuration;
            _queue = queue;
            _incomingMessageHandler = incomingMessageHandler;
            _pluginLog = pluginLog;
            _chatGui = chatGui;
            _gameInteropProvider = gameInteropProvider;
        }

        public void Enable()
        {
            try
            {
                _processChatBoxHook = _gameInteropProvider.HookFromAddress<UIModule.Delegates.ProcessChatBoxEntry>(
                    (nint)UIModule.MemberFunctionPointers.ProcessChatBoxEntry, ProcessChatBoxDetour);
                _processChatBoxHook.Enable();
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to create ProcessChatBoxEntry hook.");
                throw;
            }
        }

        private void ProcessChatBoxDetour(UIModule* uiModule, Utf8String* message, nint a4, bool saveToHistory)
        {
            try
            {
                if (!Intercept(message, saveToHistory))
                    _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Critical error in ProcessChatBox detour; passing the line to the game.");
                _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
            }
        }

        /// <summary>Returns true when the line was taken over (queued); false to hand it to the game now.</summary>
        private bool Intercept(Utf8String* message, bool saveToHistory)
        {
            // Lines Kotodama itself sends re-enter this hook synchronously.
            if (ChatSender.IsSending)
                return false;

            var queue = _queue();
            if (queue == null)
                return false;

            var text = message->ToString();
            if (_incomingMessageHandler.IsPendingOutgoing(text, false))
                return false;

            var raw = message->AsSpan();
            var decision = OutgoingRouting.Decide(
                _configuration.OutgoingMode,
                text,
                _configuration.ExcludedMessages.Contains(text),
                ChannelPrefixParser.IsChannelCommand(text),
                hasPayload: raw.IndexOf((byte)0x02) >= 0,
                queueEmpty: queue.IsIdle);

            switch (decision)
            {
                case OutgoingDecision.PassThrough:
                    return EnqueuePassThrough(queue, text, raw, saveToHistory);

                case OutgoingDecision.Translate:
                    return EnqueueTranslation(queue, text, saveToHistory);

                default:
                    return false;
            }
        }

        private bool EnqueuePassThrough(OutgoingQueue queue, string text, ReadOnlySpan<byte> raw, bool saveToHistory)
        {
            // A plain line goes to the channel active now, not when it is finally sent.
            byte[] line;
            if (text.StartsWith('/'))
            {
                line = raw.ToArray();
            }
            else
            {
                var target = Plugin.CaptureActiveChannel();
                line = target == null ? raw.ToArray() : Concat(Encoding.UTF8.GetBytes(target.Prefix + " "), raw);
            }

            var item = new OutgoingItem { Kind = OutgoingItemKind.PassThrough, Text = text, RawLine = line, SaveToHistory = saveToHistory };
            if (queue.TryEnqueue(item))
                return true;

            // Never drop a line the user typed: sending it now is better than losing it.
            _pluginLog.Warning("Outgoing queue full; sending an untranslated line immediately (out of order).");
            return false;
        }

        private bool EnqueueTranslation(OutgoingQueue queue, string text, bool saveToHistory)
        {
            var target = Plugin.CaptureActiveChannel();
            if (target == null)
            {
                _chatGui.PrintError($"[Kotodama] Not sent: the active chat channel can't be addressed explicitly. Use /tl /p … instead. Your message: {text}");
                return true;
            }

            var item = new OutgoingItem { Kind = OutgoingItemKind.Translate, Text = text, Prefix = target.Prefix, SaveToHistory = saveToHistory };
            if (!queue.TryEnqueue(item))
                _chatGui.PrintError($"[Kotodama] Not sent: too many messages are waiting to be translated. Your message: {text}");

            return true;
        }

        private static byte[] Concat(byte[] prefix, ReadOnlySpan<byte> body)
        {
            var result = new byte[prefix.Length + body.Length];
            prefix.CopyTo(result, 0);
            body.CopyTo(result.AsSpan(prefix.Length));
            return result;
        }

        public void Dispose()
        {
            _processChatBoxHook?.Dispose();
        }
    }
}
