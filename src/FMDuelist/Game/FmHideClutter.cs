using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace FMDuelist.Game;

/// <summary>
/// Less on screen in a Forbidden Memories run: the monster zones' cards show no health bars (their card's ATK and
/// DEF are what matter), and there is no energy orb (FM has no energy: every card is free).
/// </summary>
[HarmonyPatch]
static class FmHideClutter
{
    static bool FmRun => RunManager.Instance.DebugOnlyGetState()?.Players.Any(FmRules.IsFmPlayer) == true;

    /// <summary>A creature that only stands in for a monster zone (Osty, the extra zones, his zones).</summary>
    static bool IsZone(Creature? c) => c?.Monster is FmZonePet or FmRivalZone || (c?.Monster is Osty && FmRun);

    static Creature? Of(NCreatureStateDisplay d) => Traverse.Create(d).Field("_creature").GetValue<Creature>();

    [HarmonyPrefix, HarmonyPatch(typeof(NCreatureStateDisplay), nameof(NCreatureStateDisplay.AnimateIn))]
    static bool NoBarIn(NCreatureStateDisplay __instance)
    {
        if (!IsZone(Of(__instance))) return true;
        __instance.Visible = false;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(NCreatureStateDisplay), nameof(NCreatureStateDisplay.ShowNameplate))]
    static bool NoNameplate(NCreatureStateDisplay __instance) => !IsZone(Of(__instance));

    [HarmonyPostfix, HarmonyPatch(typeof(NCreatureStateDisplay), nameof(NCreatureStateDisplay.SetCreature))]
    static void NoBar(NCreatureStateDisplay __instance, Creature creature)
    {
        if (IsZone(creature)) __instance.Visible = false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(NEnergyCounter), nameof(NEnergyCounter._Ready))]
    static void NoEnergy(NEnergyCounter __instance)
    {
        if (Traverse.Create(__instance).Field("_player").GetValue<Player>() is { } p && FmRules.IsFmPlayer(p)) __instance.Visible = false;
    }
}
