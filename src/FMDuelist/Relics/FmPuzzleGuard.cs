using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using FMDuelist.Cards;

namespace FMDuelist.Relics;

/// <summary>
/// The Millennium Puzzle carries all of the mod's rules, so nothing may take it away: it can't be removed, replaced,
/// melted or traded (events and relics that would do so simply skip it).
/// </summary>
[HarmonyPatch]
static class FmPuzzleGuard
{
    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.RemoveRelicInternal))]
    static bool NoRemove(RelicModel relic)
    {
        if (relic is not MillenniumPuzzle) return true;
        Log.Info("[FMDuelist] The Millennium Puzzle can't be removed");
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.MeltRelicInternal))]
    static bool NoMelt(RelicModel relic) => relic is not MillenniumPuzzle;

    [HarmonyPostfix, HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IsTradable), MethodType.Getter)]
    static void NotTradable(RelicModel __instance, ref bool __result) { if (__instance is MillenniumPuzzle) __result = false; }
}

/// <summary>The battle-position choice shows a sword (Attack) and a shield (Defense), under "Choose your Battle Position".</summary>
[HarmonyPatch]
static class FmPositionChoiceLook
{
    static readonly Dictionary<string, Texture2D?> _tex = new();

    public static Texture2D? Tex(string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        var f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmPositionChoiceLook).Assembly.Location) ?? ".", name + ".png");
        t = System.IO.File.Exists(f) && Image.LoadFromFile(f) is { } img ? ImageTexture.CreateFromImage(img) : null;
        return _tex[name] = t;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(NChooseACardSelectionScreen), nameof(NChooseACardSelectionScreen._Ready))]
    static void Banner(NChooseACardSelectionScreen __instance)
    {
        try
        {
            var cards = Traverse.Create(__instance).Field("_cards").GetValue<IReadOnlyList<CardModel>>();
            if (cards == null || !cards.Any(c => c is FmAttackPosition or FmDefensePosition)) return;
            var banner = Traverse.Create(__instance).Field("_banner").GetValue<MegaCrit.Sts2.Core.Nodes.CommonUi.NCommonBanner>();
            banner?.label.SetTextAutoSize("Choose your Battle Position");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] position banner: " + ex.Message); }
    }
}
