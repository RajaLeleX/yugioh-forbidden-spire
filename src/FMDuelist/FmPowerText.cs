using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist;

/// <summary>While an enemy power builds its tooltip, its "Amount" is written in FM numbers (x100).</summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
static class PowerTipScopePatch
{
    [ThreadStatic] public static PowerModel? Current;
    static void Prefix(PowerModel __instance) => Current = PowerDisplayPatch.ScalesFor(__instance) ? __instance : null;
    static void Postfix(ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result) { __result = __result.ToList(); Current = null; }
}

[HarmonyPatch(typeof(LocString), nameof(LocString.Add), new[] { typeof(string), typeof(decimal) })]
static class LocAmountPatch
{
    static void Prefix(string name, ref decimal variable)
    {
        if (name == "Amount" && PowerTipScopePatch.Current != null) variable *= FmRules.EnemyScale;
    }
}
