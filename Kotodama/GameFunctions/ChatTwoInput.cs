using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// Reads ChatTwo's input state over its public IPC ("ChatTwo.GetChatInputState", a tuple of
    /// InputVisible, InputFocused, HasText, IsTyping, TextLength, ChannelType). ChatTwo keeps its
    /// input focused after sending, so an empty focused input must not count as "typing" for the
    /// translation preview. Any failure means "unknown" and the preview keeps its normal rules.
    /// </summary>
    internal sealed class ChatTwoInput
    {
        private readonly ICallGateSubscriber<object>? _state;
        private readonly IPluginLog _log;
        private bool _broken;

        public ChatTwoInput(IDalamudPluginInterface pluginInterface, IPluginLog log)
        {
            _log = log;
            try
            {
                // object return: ChatTwo's tuple contains its own enum type, which we can't reference.
                _state = pluginInterface.GetIpcSubscriber<object>("ChatTwo.GetChatInputState");
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "ChatTwo input state IPC unavailable.");
                _broken = true;
            }
        }

        /// <summary>True only when ChatTwo reports its input focused and empty.</summary>
        public bool IsFocusedAndEmpty()
        {
            if (_broken || _state == null)
                return false;

            try
            {
                var result = _state.InvokeFunc();
                if (result == null)
                    return false;

                // A boxed ValueTuple or a converted JObject both serialize as Item1..Item6.
                var token = result as JToken ?? JToken.FromObject(result);
                var focused = token["Item2"]?.Value<bool>() ?? false;
                var hasText = token["Item3"]?.Value<bool>() ?? true;
                return focused && !hasText;
            }
            catch (IpcNotReadyError)
            {
                return false; // ChatTwo not loaded.
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "ChatTwo input state IPC returned an unexpected value; ignoring it from now on.");
                _broken = true;
                return false;
            }
        }
    }
}
