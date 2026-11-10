using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EchoXIV.Interop
{
    /// <summary>
    /// Reads ChatTwo's "ChatTwo.GetChatInputState" result: the tuple (InputVisible, InputFocused,
    /// HasText, IsTyping, TextLength, ChannelType).
    /// <para>
    /// Dalamud converts the tuple into a Newtonsoft JObject from <b>its own</b> Newtonsoft
    /// assembly, which isn't type-identical to any Newtonsoft the plugin sees (casts fail and
    /// re-serializing yields empty arrays). So nothing here touches Newtonsoft types: a real tuple
    /// is read through <see cref="ITuple"/>, anything else through its JSON text (JObject.ToString())
    /// parsed with System.Text.Json. The four booleans are taken in document order.
    /// </para>
    /// </summary>
    public static class ChatInputStateReader
    {
        /// <summary>Returns (focused, hasText), or null when the value doesn't hold four booleans.</summary>
        public static (bool Focused, bool HasText)? TryRead(object? result)
        {
            if (result == null)
                return null;

            var bools = new List<bool>(4);
            if (result is ITuple tuple)
            {
                for (var i = 0; i < tuple.Length && bools.Count < 4; i++)
                {
                    if (tuple[i] is bool b)
                        bools.Add(b);
                }
            }
            else
            {
                var text = result.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                try
                {
                    using var document = JsonDocument.Parse(text);
                    Collect(document.RootElement, bools);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return bools.Count < 4 ? null : (bools[1], bools[2]);
        }

        private static void Collect(JsonElement element, List<bool> bools)
        {
            if (bools.Count >= 4)
                return;

            switch (element.ValueKind)
            {
                case JsonValueKind.True:
                    bools.Add(true);
                    break;
                case JsonValueKind.False:
                    bools.Add(false);
                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Collect(property.Value, bools);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Collect(item, bools);
                    break;
            }
        }

        /// <summary>A short description of the value for logs.</summary>
        public static string Describe(object? result) =>
            result == null ? "null" : $"{result.GetType().FullName}: {result}";
    }
}
