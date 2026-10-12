using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Powers;

namespace FMDuelist.Game;

/// <summary>
/// Plating for the duelist: every level gives your monsters +200 DEF (3 Plating = +600 DEF) instead of end-of-turn
/// Block. It still drops by 1 at the start of each of your turns, as in regular StS. Enemy Plating is unchanged.
/// </summary>
public static class FmPlating
{
    public const int DefPerLevel = 200;

    public static int DefBonus(Player? p) => p?.Creature.GetPower<PlatingPower>() is { } pl && pl.Amount > 0 ? (int)pl.Amount * DefPerLevel : 0;

    public static void ApplyLoc(LocManager loc) =>
        loc.GetTable("powers").MergeWith(new Dictionary<string, string>
        {
            ["PLATING_POWER.description"] = "Duel Master K: each [gold]Plating[/gold] gives your monsters [blue]+200[/blue] DEF. Enemies: gain [gold]Block[/gold] at the end of their turn. [gold]Plating[/gold] is reduced by [blue]1[/blue] at the start of each turn.",
            ["PLATING_POWER.smartDescription"] = "Duel Master K: your monsters get [blue]+200[/blue] DEF per [gold]Plating[/gold]. Enemies: gain [blue]{Amount}[/blue] [gold]Block[/gold] at the end of their turn. [gold]Plating[/gold] is reduced by [blue]{Decrement}[/blue] at the start of each turn.",
        });
}

/// <summary>The duelist's Plating doesn't give Block (its DEF bonus is counted on the monsters instead).</summary>
[HarmonyPatch(typeof(PlatingPower), nameof(PlatingPower.BeforeSideTurnEndEarly))]
static class PlatingNoBlockPatch
{
    static bool Prefix(PlatingPower __instance, ref Task __result)
    {
        if (__instance.Owner?.Player is not { } p || !FmRules.IsFmPlayer(p)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
