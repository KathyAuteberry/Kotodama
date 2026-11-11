using System;
using System.Text;

namespace EchoXIV
{
    /// <summary>
    /// Recognises the local player as the sender of a chat line. The sender text can carry a party
    /// number or other game glyph (Unicode private use area) in front of the name, and a home
    /// world after it, either as "@World" or glued on ("First LastWorld").
    /// </summary>
    public static class LocalSender
    {
        public static bool Is(string? sender, string? localName)
        {
            if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(localName))
                return false;

            var name = Clean(sender);
            var local = Clean(localName);
            if (local.Length == 0 || !name.StartsWith(local, StringComparison.Ordinal))
                return false;

            if (name.Length == local.Length)
                return true;

            // What follows the name must be a world: "@World", "＠World" or a glued "World"
            // (capitalised), never more of a longer name ("Kath Auteberryson").
            var next = name[local.Length];
            return next is '@' or '＠' || char.IsUpper(next);
        }

        private static string Clean(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c is >= '' and <= '' || char.IsControl(c))
                    continue;
                builder.Append(c);
            }

            return builder.ToString().Trim();
        }
    }
}
