using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Subscribes to Dalamud's nameplate update event and prefixes paired players' titles with a
/// marker so the user can see at a glance who Aethernet is syncing. We avoid hard-replacing the
/// name itself — overwriting the visible name confuses /target macros and screen-reader-style
/// add-ons.
/// </summary>
public sealed class NameplateDecorator : IDisposable
{
    private const string PairedMarker      = " "; // SeIconChar.LinkMarker
    private const string VisibleMarker     = " "; // SeIconChar.BoxedStar

    private readonly INamePlateGui _nameplate;
    private readonly PairManager _pairs;
    private readonly VisibleUserManager _visible;
    private readonly ILogger<NameplateDecorator> _log;

    public NameplateDecorator(
        INamePlateGui nameplate, PairManager pairs, VisibleUserManager visible,
        ILogger<NameplateDecorator> log)
    {
        _nameplate = nameplate; _pairs = pairs; _visible = visible; _log = log;
        _nameplate.OnNamePlateUpdate += OnUpdate;
    }

    private void OnUpdate(INamePlateUpdateContext ctx, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        foreach (var h in handlers)
        {
            if (h.NamePlateKind != NamePlateKind.PlayerCharacter) continue;
            if (h.PlayerCharacter is null) continue;

            var ident = $"{h.PlayerCharacter.Name.TextValue}@{h.PlayerCharacter.HomeWorld.RowId}";

            // Is this player one of our pairs?
            var pair = _pairs.All.FirstOrDefault(p => p.RemoteIdent == ident);
            if (pair is null) continue;

            var visible = _visible.IsVisible(pair.Pair.User.UID);
            var marker  = visible ? VisibleMarker : PairedMarker;

            // Prefix the title rather than the name itself.
            // NamePlateQuotedParts.TextWrapped was renamed to .Text in Dalamud 15.
            h.TitleParts.Text = new Dalamud.Game.Text.SeStringHandling.SeString(
                new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(marker + (h.TitleParts.Text?.TextValue ?? "")));
        }
    }

    public void Dispose() { _nameplate.OnNamePlateUpdate -= OnUpdate; }
}
