using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace FMDuelist;

/// <summary>FM cards cost nothing, so relics and potions that give energy never appear in FM runs.</summary>
static partial class FmEnergyFilter
{
    static HashSet<Type>? _relics, _potions, _events;

    static HashSet<Type> Resolve(string[] names, string ns)
    {
        var asm = typeof(RelicModel).Assembly;
        var set = new HashSet<Type>();
        foreach (var n in names)
        {
            var t = asm.GetType($"MegaCrit.Sts2.Core.Models.{ns}.{n}");
            if (t != null) set.Add(t); else Log.Warn($"[FMDuelist] energy filter: {ns}.{n} not found");
        }
        return set;
    }

    public static bool IsEnergyRelic(RelicModel r) => (_relics ??= Resolve(RelicNames, "Relics")).Contains(r.GetType());
    public static bool IsEnergyPotion(PotionModel p) => (_potions ??= Resolve(PotionNames, "Potions")).Contains(p.GetType());
    /// <summary>A random allowed potion (same rarity when possible) to stand in for a removed one.</summary>
    public static PotionModel? Replacement(Player player, PotionModel removed)
    {
        var all = ModelDb.PotionPool<MegaCrit.Sts2.Core.Models.PotionPools.SharedPotionPool>().GetUnlockedPotions(player.UnlockState)
            .Where(p => !IsEnergyPotion(p)).ToList();
        if (all.Count == 0) return null;
        var same = all.Where(p => p.Rarity == removed.Rarity).ToList();
        var pick = (same.Count > 0 ? same : all)[Random.Shared.Next(same.Count > 0 ? same.Count : all.Count)];
        return pick.ToMutable();
    }

    /// <summary>Removed potions already in your belt are swapped (e.g. ones picked up before this rule).</summary>
    public static async Task CleanBelt(Player player)
    {
        foreach (var p in player.Potions.Where(IsEnergyPotion).ToList())
        {
            var swap = Replacement(player, p);
            await MegaCrit.Sts2.Core.Commands.PotionCmd.Discard(p);
            if (swap != null) await MegaCrit.Sts2.Core.Commands.PotionCmd.TryToProcure(swap, player);
            Log.Info($"[FMDuelist] removed {p.Id.Entry} from the belt");
        }
    }

    public static bool IsRemovedEvent(EventModel e) => (_events ??= Resolve(EventNames, "Events")).Contains(e.GetType());
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IsAllowed))]
static class EnergyRelicPatch
{
    static void Postfix(RelicModel __instance, IRunState runState, ref bool __result)
    {
        if (__result && runState.Players.Any(FmRules.IsFmPlayer) && FmEnergyFilter.IsEnergyRelic(__instance)) __result = false;
    }
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IsAllowedAtNeow))]
static class EnergyRelicNeowPatch
{
    static void Postfix(RelicModel __instance, Player player, ref bool __result)
    {
        if (__result && FmRules.IsFmPlayer(player) && FmEnergyFilter.IsEnergyRelic(__instance)) __result = false;
    }
}

[HarmonyPatch(typeof(PotionFactory), nameof(PotionFactory.GetPotionOptions))]
static class EnergyPotionPatch
{
    static void Postfix(Player player, ref IEnumerable<PotionModel> __result)
    {
        if (FmRules.IsFmPlayer(player)) __result = __result.Where(p => !FmEnergyFilter.IsEnergyPotion(p)).ToList();
    }
}

[HarmonyPatch(typeof(EventModel), nameof(EventModel.IsAllowed))]
static class UpgradeEventPatch
{
    static void Postfix(EventModel __instance, IRunState runState, ref bool __result)
    {
        if (__result && runState.Players.Any(FmRules.IsFmPlayer) && FmEnergyFilter.IsRemovedEvent(__instance)) __result = false;
    }
}

/// <summary>The game's menu music isn't playing (FM track 00 is), so its menu music parameters are skipped.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager), nameof(MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager.UpdateMusicParameter))]
static class MenuMusicParamPatch
{
    static bool Prefix(string parameter) => parameter != "menu_progress";
}


/// <summary>
/// Removed potions (energy, upgrades, Strike-only) are filtered out of the potion pools themselves in FM runs, which
/// covers rewards, shops, events and relics; anything that still slips through is swapped when you'd receive it.
/// </summary>
[HarmonyPatch(typeof(PotionPoolModel), nameof(PotionPoolModel.GetUnlockedPotions))]
static class RemovedPotionPoolPatch
{
    static void Postfix(ref IEnumerable<PotionModel> __result)
    {
        if (FmRules.RunActive) __result = __result.Where(p => !FmEnergyFilter.IsEnergyPotion(p)).ToList();
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.PotionCmd), nameof(MegaCrit.Sts2.Core.Commands.PotionCmd.TryToProcure), new[] { typeof(PotionModel), typeof(Player), typeof(int) })]
static class RemovedPotionProcurePatch
{
    static void Prefix(ref PotionModel potion, Player player)
    {
        try
        {
            if (!FmRules.IsFmPlayer(player) || !FmEnergyFilter.IsEnergyPotion(potion)) return;
            var swap = FmEnergyFilter.Replacement(player, potion);
            if (swap != null) { Log.Info($"[FMDuelist] {potion.Id.Entry} swapped for {swap.Id.Entry}"); potion = swap; }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] potion swap: " + ex.Message); }
    }
}
