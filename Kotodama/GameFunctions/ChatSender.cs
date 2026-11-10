using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using EchoXIV.Outgoing;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// The game-facing end of the outgoing queue (docs/PLAN.md P1.3). Every send is a fresh
    /// <c>UIModule.Instance()->ProcessChatBoxEntry(line, 0, saveToHistory)</c> on the framework
    /// thread with an explicit channel prefix. No hooked arguments (UIModule*, a4) are ever
    /// captured or replayed.
    /// </summary>
    internal sealed unsafe class ChatSender : IOutgoingSink
    {
        // Framework thread only: our own sends re-enter the chat-box hook synchronously.
        private static int _sendingDepth;

        /// <summary>True while Kotodama itself is handing a line to the game (framework thread).</summary>
        public static bool IsSending => _sendingDepth > 0;

        private readonly IFramework _framework;
        private readonly IChatGui _chatGui;
        private readonly IPluginLog _log;
        private readonly IncomingMessageHandler? _incoming;
        private readonly CancellationToken _shutdown;

        public ChatSender(IFramework framework, IChatGui chatGui, IPluginLog log, IncomingMessageHandler? incoming, CancellationToken shutdown)
        {
            _framework = framework;
            _chatGui = chatGui;
            _log = log;
            _incoming = incoming;
            _shutdown = shutdown;
        }

        public Task SendAsync(OutgoingSend send)
        {
            return _framework.RunOnFrameworkThread(() =>
            {
                if (_shutdown.IsCancellationRequested)
                    return;

                if (send.Translation != null)
                {
                    // Lets the translated chat window show our echo with the original text.
                    var shown = OutgoingGuards.Sanitize(send.Translation.TextToSend);
                    _incoming?.RegisterPendingOutgoing(
                        shown,
                        send.Item.Text,
                        send.Translation.NativeText,
                        shown != send.Translation.NativeText ? shown : null);
                }

                if (send.RawLine != null)
                    SendBytes(send.RawLine, send.Item.SaveToHistory);
                else if (!string.IsNullOrEmpty(send.Line))
                    SendLine(send.Line, send.Item.SaveToHistory);
            });
        }

        public Task ReportFailureAsync(OutgoingItem item, OutgoingFailureKind reason, bool sentOriginal)
        {
            _log.Warning($"Outgoing translation failed ({reason}); sentOriginal={sentOriginal}: '{item.Text}'");
            return _framework.RunOnFrameworkThread(() =>
            {
                if (_shutdown.IsCancellationRequested)
                    return;

                var why = OutgoingGuards.Describe(reason);
                if (sentOriginal)
                    _chatGui.Print($"[Kotodama] Sent your original message untranslated: {why}.");
                else
                    _chatGui.PrintError($"[Kotodama] Not sent: {why}. Your message: {item.Text}");
            });
        }

        /// <summary>Tells the user a line could not even be queued (queue full or shutting down).</summary>
        public void ReportNotQueued(string text)
        {
            _chatGui.PrintError($"[Kotodama] Not sent: too many messages are waiting to be translated. Your message: {text}");
        }

        private void SendLine(string line, bool saveToHistory)
        {
            // Control characters (including SeString payload markers) were already removed by
            // OutgoingGuards.Sanitize; the game's SanitizeString is not used because its handling of
            // Japanese punctuation is undocumented.
            var utf8 = Utf8String.FromString(line);
            Send(utf8, saveToHistory);
        }

        private void SendBytes(byte[] raw, bool saveToHistory)
        {
            var utf8 = Utf8String.FromSequence(raw);
            Send(utf8, saveToHistory);
        }

        private void Send(Utf8String* utf8, bool saveToHistory)
        {
            _sendingDepth++;
            try
            {
                UIModule.Instance()->ProcessChatBoxEntry(utf8, 0, saveToHistory);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error while sending a chat message.");
                _chatGui.PrintError("[Kotodama] Error sending message.");
            }
            finally
            {
                _sendingDepth--;
                utf8->Dtor(true);
            }
        }
    }
}
