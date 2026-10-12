using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace FMDuelist;

/// <summary>Enemy powers whose number is damage or Block (Strength, Thorns, Poison...) show it in FM numbers (x100), matching what they really do.</summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.DisplayAmount), MethodType.Getter)]
static class PowerDisplayPatch
{
    public static bool ScalesFor(PowerModel p) =>
        FmRules.RunActive && p.Owner != null && Scaled.Contains(p.GetType()) && (p.Owner.IsEnemy || p is PoisonPower);

    public static readonly HashSet<Type> Scaled = new()
    {
        typeof(StrengthPower), typeof(DexterityPower), typeof(ThornsPower), typeof(PoisonPower),
        typeof(BlockNextTurnPower), typeof(ConstrictPower),
    };

    static void Postfix(PowerModel __instance, ref int __result)
    {
        if (!FmRules.RunActive || __instance.Owner == null) return;
        if (!Scaled.Contains(__instance.GetType())) return;
        bool enemy = __instance.Owner.IsEnemy;
        bool poisonOnUs = __instance is PoisonPower && !enemy;
        if (enemy || poisonOnUs) __result *= FmRules.EnemyScale;
    }
}
