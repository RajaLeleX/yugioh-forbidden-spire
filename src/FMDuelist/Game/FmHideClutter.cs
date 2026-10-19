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
        if (!IsZone(creature)) return;
        __instance.Visible = false;
        // the game shows it again on HP and block changes: keep it hidden for good
        var d = __instance;
        if (!d.HasMeta("fm_hidden"))
        {
            d.SetMeta("fm_hidden", true);
            d.VisibilityChanged += () => { if (GodotObject.IsInstanceValid(d) && d.Visible && IsZone(Of(d))) Callable.From(() => { if (GodotObject.IsInstanceValid(d)) d.Visible = false; }).CallDeferred(); };
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(NEnergyCounter), nameof(NEnergyCounter._Ready))]
    static void NoEnergy(NEnergyCounter __instance)
    {
        if (Traverse.Create(__instance).Field("_player").GetValue<Player>() is { } p && FmRules.IsFmPlayer(p)) __instance.Visible = false;
    }
}

/// <summary>
/// Osty never shows in a Forbidden Memories run: monster zone 1 uses the game's Osty underneath (its mechanics keep
/// working), but its body is hidden from the moment its node exists (summon fade-in, revive and death included) and
/// for as long as it lives; only the zone's card is ever seen.
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom), nameof(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.AddCreature))]
static class FmNoOstyPatch
{
    static bool FmRun => RunManager.Instance.DebugOnlyGetState()?.Players.Any(FmRules.IsFmPlayer) == true;

    static void Postfix(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom __instance, Creature creature)
    {
        try
        {
            if (!(creature.Monster is Osty or FmZonePet) || !FmRun) return;
            if (__instance.GetCreatureNode(creature) is not { } node || Engine.GetMainLoop() is not SceneTree tree) return;
            void Hide()
            {
                if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) { tree.ProcessFrame -= Hide; return; }
                if (node.Visuals.GetCurrentBody() is { } body && body.Visible) body.Visible = false;
            }
            Hide();
            tree.ProcessFrame += Hide;
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] hide osty: " + ex.Message); }
    }
}

/// <summary>...and Osty is never heard: its own sounds and the Necrobinder's summon sound stay silent in FM runs.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.SfxCmd), nameof(MegaCrit.Sts2.Core.Commands.SfxCmd.Play), new[] { typeof(string), typeof(float) })]
static class FmNoOstySoundPatch
{
    static bool Prefix(string sfx)
    {
        if (sfx == null || !(sfx.Contains("/osty/") || sfx.Contains("necrobinder_summon"))) return true;
        return RunManager.Instance.DebugOnlyGetState()?.Players.Any(FmRules.IsFmPlayer) != true;
    }
}
