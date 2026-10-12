using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace FMDuelist.Game;

/// <summary>Exoskeletons don't work with the duel damage rules, so they're gone: no Exoskeleton fights in the Hive.</summary>
[HarmonyPatch(typeof(Hive), nameof(Hive.GenerateAllEncounters))]
static class NoExoskeletonEncountersPatch
{
    static void Postfix(ref IEnumerable<EncounterModel> __result) =>
        __result = __result.Where(e => e is not ExoskeletonsNormal and not ExoskeletonsWeak).ToList();
}

/// <summary>Runs saved before this change may still have an Exoskeleton room: its Exoskeletons become Bowlbugs.</summary>
[HarmonyPatch]
static class ExoskeletonSwapPatch
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ExoskeletonsNormal), "GenerateMonsters");
        yield return AccessTools.Method(typeof(ExoskeletonsWeak), "GenerateMonsters");
    }

    static void Postfix(ref IReadOnlyList<(MonsterModel, string?)> __result) =>
        __result = __result.Select(m => m.Item1 is Exoskeleton ? ((MonsterModel)ModelDb.Monster<BowlbugRock>().ToMutable(), m.Item2) : m).ToList();
}
