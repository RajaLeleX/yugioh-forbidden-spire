using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace FMDuelist.Game;

/// <summary>No card upgrades in Forbidden Memories, so the "View Upgrades" toggles are hidden in FM runs.</summary>
[HarmonyPatch]
static class NoUpgradeTogglePatch
{
    static void Hide(Node? n)
    {
        if (n is CanvasItem c) { c.Visible = false; if (c is Control ctl) ctl.MouseFilter = Control.MouseFilterEnum.Ignore; }
    }

    // card inspect screen (clicking a card)
    [HarmonyPostfix, HarmonyPatch(typeof(NInspectCardScreen), "UpdateCardDisplay")]
    static void Inspect(NInspectCardScreen __instance)
    {
        if (!FmRules.RunActive) return;
        try { Hide(__instance.GetNodeOrNull("%Upgrade")); } catch (Exception ex) { Log.Warn("[FMDuelist] inspect toggle: " + ex.Message); }
    }

    // deck / draw / discard views
    [HarmonyPostfix, HarmonyPatch(typeof(NCardsViewScreen), nameof(NCardsViewScreen._Ready))]
    static void CardsView(NCardsViewScreen __instance)
    {
        if (!FmRules.RunActive) return;
        try
        {
            Hide(__instance.GetNodeOrNull("%Upgrades"));
            Hide(__instance.GetNodeOrNull("%ViewUpgradesLabel"));
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] view toggle: " + ex.Message); }
    }
}
