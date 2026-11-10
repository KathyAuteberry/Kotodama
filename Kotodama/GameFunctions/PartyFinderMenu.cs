using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EchoXIV.GameFunctions
{
    /// <summary>A Party Finder listing read from the detail window.</summary>
    internal sealed record PartyFinderListing(IncomingText Description, string RawDescription, string? Recruiter, string? Duty);

    /// <summary>
    /// Adds "Translate" to the context menu of the Party Finder listing detail window
    /// (LookingForGroupDetail) and reads the listing when it's clicked. The addon is read
    /// synchronously in the click callback (framework thread); translation happens elsewhere.
    /// </summary>
    internal sealed unsafe class PartyFinderMenu : IDisposable
    {
        private const string DetailAddon = "LookingForGroupDetail";

        private readonly IContextMenu _contextMenu;
        private readonly IPluginLog _log;
        private readonly Func<bool> _enabled;
        private readonly Action<PartyFinderListing> _onTranslate;
        private readonly MenuItem _item;

        public PartyFinderMenu(IContextMenu contextMenu, IPluginLog log, Func<bool> enabled, Action<PartyFinderListing> onTranslate)
        {
            _contextMenu = contextMenu;
            _log = log;
            _enabled = enabled;
            _onTranslate = onTranslate;
            _item = new MenuItem
            {
                Name = "Translate (Kotodama)",
                UseDefaultPrefix = true,
                OnClicked = OnClicked,
            };
            _contextMenu.OnMenuOpened += OnMenuOpened;
        }

        private void OnMenuOpened(IMenuOpenedArgs args)
        {
            if (args.AddonName == DetailAddon && _enabled())
                args.AddMenuItem(_item);
        }

        private void OnClicked(IMenuItemClickedArgs args)
        {
            try
            {
                if (args.AddonPtr == nint.Zero)
                    return;

                var addon = (AddonLookingForGroupDetail*)args.AddonPtr;
                var description = SeString.Parse(addon->DescriptionString.AsSpan());
                var listing = new PartyFinderListing(
                    PartyFinderDescription.Parse(ToPieces(description)),
                    description.TextValue,
                    ReadText(addon->PartyLeaderTextNode),
                    ReadText(addon->DutyNameTextNode));
                _onTranslate(listing);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Couldn't read the Party Finder listing.");
            }
        }

        private static IEnumerable<PartyFinderPiece> ToPieces(SeString text)
        {
            foreach (var payload in text.Payloads)
            {
                switch (payload)
                {
                    case IconPayload { Icon: BitmapFontIcon.AutoTranslateBegin }:
                        yield return new PartyFinderPiece(PartyFinderPieceKind.PhraseBegin);
                        break;
                    case IconPayload { Icon: BitmapFontIcon.AutoTranslateEnd }:
                        yield return new PartyFinderPiece(PartyFinderPieceKind.PhraseEnd);
                        break;
                    case AutoTranslatePayload autoTranslate:
                        yield return new PartyFinderPiece(PartyFinderPieceKind.Phrase, autoTranslate.Text ?? string.Empty);
                        break;
                    case NewLinePayload:
                        yield return new PartyFinderPiece(PartyFinderPieceKind.NewLine);
                        break;
                    case TextPayload textPayload:
                        yield return new PartyFinderPiece(PartyFinderPieceKind.Text, textPayload.Text ?? string.Empty);
                        break;
                }
            }
        }

        private static string? ReadText(AtkTextNode* node)
        {
            if (node == null)
                return null;

            var text = SeString.Parse(node->NodeText.AsSpan()).TextValue.Trim();
            return text.Length == 0 ? null : text;
        }

        public void Dispose() => _contextMenu.OnMenuOpened -= OnMenuOpened;
    }
}
