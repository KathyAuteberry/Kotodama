using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EchoXIV.Outgoing
{
    public enum ChatChannelKind
    {
        Say,
        Party,
        Alliance,
        Yell,
        Shout,
        FreeCompany,
        PvpTeam,
        NoviceNetwork,
        Linkshell,
        CrossWorldLinkshell,
        Tell,

        /// <summary>
        /// "/r": reply to the last tell. Resolved to a <see cref="Tell"/> target when the line is
        /// entered (the plugin looks up the last tell partner), never at send time.
        /// </summary>
        Reply,

        /// <summary>"/e": local echo only. Useful to preview a translation without sending it.</summary>
        Echo,
    }

    /// <summary>
    /// Where an outgoing line goes, captured when the line is entered. Sending always uses
    /// <see cref="Prefix"/> explicitly, so the message cannot end up in whatever channel happens
    /// to be active when the translation finishes (docs/AUDIT.md §3.3).
    /// </summary>
    public sealed record ChannelTarget(ChatChannelKind Kind, int Index = 0, string? TellRecipient = null)
    {
        public static ChannelTarget Tell(string name, string? world) =>
            new(ChatChannelKind.Tell, TellRecipient: string.IsNullOrWhiteSpace(world) ? name.Trim() : $"{name.Trim()}@{world.Trim()}");

        /// <summary>The chat command that sends to this channel, e.g. <c>/p</c>, <c>/l3</c>, <c>/t First Last@World</c>.</summary>
        public string Prefix => Kind switch
        {
            ChatChannelKind.Say => "/s",
            ChatChannelKind.Party => "/p",
            ChatChannelKind.Alliance => "/a",
            ChatChannelKind.Yell => "/y",
            ChatChannelKind.Shout => "/sh",
            ChatChannelKind.FreeCompany => "/fc",
            ChatChannelKind.PvpTeam => "/pvpteam",
            ChatChannelKind.NoviceNetwork => "/beginner",
            ChatChannelKind.Linkshell => $"/l{Index}",
            ChatChannelKind.CrossWorldLinkshell => $"/cwl{Index}",
            ChatChannelKind.Tell => $"/t {TellRecipient}",
            ChatChannelKind.Reply => "/r",
            ChatChannelKind.Echo => "/e",
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
        };

        /// <summary>The full line to hand to the game.</summary>
        public string ToCommand(string body) => $"{Prefix} {body}";
    }

    public enum ChannelParseError
    {
        None,
        EmptyMessage,
        UnsupportedCommand,
        MissingTellRecipient,
    }

    /// <summary>
    /// Result of parsing <c>/tl</c> arguments. <see cref="Target"/> is null (with no error) when
    /// the text has no channel prefix: the caller then captures the active channel.
    /// </summary>
    public sealed record ChannelParseResult(ChannelTarget? Target, string Body, ChannelParseError Error, string? Command = null)
    {
        public bool IsError => Error != ChannelParseError.None;
    }

    /// <summary>Parses an optional chat-channel prefix in front of a message.</summary>
    public static class ChannelPrefixParser
    {
        private static readonly Dictionary<string, Func<ChannelTarget>> SimpleCommands = BuildSimpleCommands();

        public static ChannelParseResult Parse(string input)
        {
            var text = (input ?? string.Empty).Trim();
            if (text.Length == 0)
                return new ChannelParseResult(null, string.Empty, ChannelParseError.EmptyMessage);

            if (!StartsWithSlash(text))
                return new ChannelParseResult(null, text, ChannelParseError.None);

            var (command, rest) = SplitFirstToken(text);
            var key = NormalizeCommand(command);

            if (key is "/t" or "/tell")
                return ParseTell(rest, key);

            if (!SimpleCommands.TryGetValue(key, out var target))
                return new ChannelParseResult(null, text, ChannelParseError.UnsupportedCommand, key);

            var body = rest.Trim();
            return body.Length == 0
                ? new ChannelParseResult(target(), string.Empty, ChannelParseError.EmptyMessage, key)
                : new ChannelParseResult(target(), body, ChannelParseError.None, key);
        }

        /// <summary>True when the line starts with a chat-channel command (it is a chat line, not an action).</summary>
        public static bool IsChannelCommand(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (!StartsWithSlash(trimmed))
                return false;

            var key = NormalizeCommand(SplitFirstToken(trimmed).Command);
            return key is "/t" or "/tell" || SimpleCommands.ContainsKey(key);
        }

        /// <summary>
        /// Maps <c>RaptureShellModule.ChatType</c> (the active chat input channel) to a target.
        /// Returns null for channels Kotodama does not know how to address explicitly.
        /// </summary>
        public static ChannelTarget? FromInputChannel(int chatType, string? tellName, string? tellWorld) => chatType switch
        {
            0 or 17 or 18 => string.IsNullOrWhiteSpace(tellName) ? null : ChannelTarget.Tell(tellName, tellWorld),
            1 => new ChannelTarget(ChatChannelKind.Say),
            2 => new ChannelTarget(ChatChannelKind.Party),
            3 => new ChannelTarget(ChatChannelKind.Alliance),
            4 => new ChannelTarget(ChatChannelKind.Yell),
            5 => new ChannelTarget(ChatChannelKind.Shout),
            6 => new ChannelTarget(ChatChannelKind.FreeCompany),
            7 => new ChannelTarget(ChatChannelKind.PvpTeam),
            8 => new ChannelTarget(ChatChannelKind.NoviceNetwork),
            >= 9 and <= 16 => new ChannelTarget(ChatChannelKind.CrossWorldLinkshell, chatType - 8),
            >= 19 and <= 26 => new ChannelTarget(ChatChannelKind.Linkshell, chatType - 18),
            _ => null,
        };

        private static ChannelParseResult ParseTell(string rest, string command)
        {
            var body = rest.Trim();
            if (body.Length == 0)
                return new ChannelParseResult(null, string.Empty, ChannelParseError.MissingTellRecipient, command);

            string recipient;
            string message;

            if (body[0] == '"')
            {
                var close = body.IndexOf('"', 1);
                if (close < 0)
                    return new ChannelParseResult(null, string.Empty, ChannelParseError.MissingTellRecipient, command);
                recipient = body[1..close].Trim();
                message = body[(close + 1)..].Trim();
            }
            else
            {
                var (first, afterFirst) = SplitFirstToken(body);
                var (second, afterSecond) = SplitFirstToken(afterFirst.TrimStart());

                if ((first.StartsWith('<') && first.EndsWith('>')) || first.Contains('@'))
                {
                    // <t>, <r>, ... placeholders, or a single token already carrying @World.
                    recipient = first;
                    message = afterFirst.Trim();
                }
                else if (second.Length > 0 && (second.Contains('@') || afterSecond.Trim().Length > 0))
                {
                    // First Last@World message, or First Last message (same world).
                    recipient = $"{first} {second}";
                    message = afterSecond.Trim();
                }
                else
                {
                    // "/t Kath hi": a character name always has two parts, so this is ambiguous.
                    return new ChannelParseResult(null, string.Empty, ChannelParseError.MissingTellRecipient, command);
                }
            }

            if (recipient.Length == 0)
                return new ChannelParseResult(null, string.Empty, ChannelParseError.MissingTellRecipient, command);

            var target = new ChannelTarget(ChatChannelKind.Tell, TellRecipient: recipient);
            return message.Length == 0
                ? new ChannelParseResult(target, string.Empty, ChannelParseError.EmptyMessage, command)
                : new ChannelParseResult(target, message, ChannelParseError.None, command);
        }

        private static bool StartsWithSlash(string text) => text.Length > 0 && (text[0] == '/' || text[0] == '／');

        /// <summary>Full-width forms typed with a Japanese IME (／ｐ) are treated like ASCII.</summary>
        private static string NormalizeCommand(string command) =>
            command.Normalize(NormalizationForm.FormKC).ToLower(CultureInfo.InvariantCulture);

        /// <summary>Splits off the first whitespace-delimited token (any Unicode whitespace, including U+3000).</summary>
        private static (string Command, string Remainder) SplitFirstToken(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    return (text[..i], text[(i + 1)..]);
            }

            return (text, string.Empty);
        }

        private static Dictionary<string, Func<ChannelTarget>> BuildSimpleCommands()
        {
            var map = new Dictionary<string, Func<ChannelTarget>>(StringComparer.Ordinal)
            {
                ["/s"] = () => new ChannelTarget(ChatChannelKind.Say),
                ["/say"] = () => new ChannelTarget(ChatChannelKind.Say),
                ["/p"] = () => new ChannelTarget(ChatChannelKind.Party),
                ["/party"] = () => new ChannelTarget(ChatChannelKind.Party),
                ["/a"] = () => new ChannelTarget(ChatChannelKind.Alliance),
                ["/alliance"] = () => new ChannelTarget(ChatChannelKind.Alliance),
                ["/y"] = () => new ChannelTarget(ChatChannelKind.Yell),
                ["/yell"] = () => new ChannelTarget(ChatChannelKind.Yell),
                ["/sh"] = () => new ChannelTarget(ChatChannelKind.Shout),
                ["/shout"] = () => new ChannelTarget(ChatChannelKind.Shout),
                ["/fc"] = () => new ChannelTarget(ChatChannelKind.FreeCompany),
                ["/freecompany"] = () => new ChannelTarget(ChatChannelKind.FreeCompany),
                ["/pvpteam"] = () => new ChannelTarget(ChatChannelKind.PvpTeam),
                ["/beginner"] = () => new ChannelTarget(ChatChannelKind.NoviceNetwork),
                ["/e"] = () => new ChannelTarget(ChatChannelKind.Echo),
                ["/echo"] = () => new ChannelTarget(ChatChannelKind.Echo),
                ["/r"] = () => new ChannelTarget(ChatChannelKind.Reply),
                ["/reply"] = () => new ChannelTarget(ChatChannelKind.Reply),
            };

            for (var i = 1; i <= 8; i++)
            {
                var index = i;
                map[$"/l{i}"] = () => new ChannelTarget(ChatChannelKind.Linkshell, index);
                map[$"/linkshell{i}"] = () => new ChannelTarget(ChatChannelKind.Linkshell, index);
                map[$"/cwl{i}"] = () => new ChannelTarget(ChatChannelKind.CrossWorldLinkshell, index);
                map[$"/cwlinkshell{i}"] = () => new ChannelTarget(ChatChannelKind.CrossWorldLinkshell, index);
            }

            return map;
        }
    }
}
