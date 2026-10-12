using FMDuelist.Game;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;

namespace FMDuelist.Game;

/// <summary>Rest site "Cut": remove cards from the deck (replaces Smith; FM cards have no upgrades): up to 1 in Act 1, 2 in Act 2, 3 in Act 3.</summary>
public sealed class FmCutRestSiteOption : RestSiteOption
{
    /// <summary>How many cards Cut removes: one more per act (Act 1: 1, Act 2: 2, Act 3: 3).</summary>
    public static int MaxCuts(Player owner) => Math.Clamp(owner.RunState.CurrentActIndex + 1, 1, 3);
    public static readonly string SmithIcon = ImageHelper.GetImagePath("ui/rest_site/option_smith.png");

    public FmCutRestSiteOption(Player owner) : base(owner) { }

    public override string OptionId => "FMCUT";
    public override IEnumerable<string> AssetPaths => new[] { SmithIcon };
    public override bool IsEnabled => Owner.Deck.Cards.Count > FmRules.MinDeckFor(Owner);

    public override async Task<bool> OnSelect()
    {
        int allowed = Math.Min(MaxCuts(Owner), Owner.Deck.Cards.Count - FmRules.MinDeckFor(Owner));
        if (allowed <= 0) return false;
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1, allowed)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };
        var picked = (await CardSelectCmd.FromDeckForRemoval(Owner, prefs)).ToList();
        if (picked.Count == 0) return false;
        await CardPileCmd.RemoveFromDeck(picked);
        return true;
    }
}

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Icon), MethodType.Getter)]
static class CutIconPatch
{
    static bool Prefix(RestSiteOption __instance, ref Texture2D __result)
    {
        if (__instance is not FmCutRestSiteOption) return true;
        __result = PreloadManager.Cache.GetTexture2D(FmCutRestSiteOption.SmithIcon);
        return false;
    }
}
