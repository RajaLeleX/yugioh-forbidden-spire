using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist;

/// <summary>
/// In FM runs, relic and potion text shows damage, Block, heal, HP-loss and max-HP numbers at x100, matching what they
/// actually do (the effects are scaled by the Millennium Puzzle / FmBalance). Percentages are left alone.
/// </summary>
static class FmItemText
{
    static readonly AccessTools.FieldRef<DynamicVar, AbstractModel?> Owner = AccessTools.FieldRefAccess<DynamicVar, AbstractModel?>("_owner");

    public static bool Scales(DynamicVar v)
    {
        if (!FmRules.RunActive) return false;
        bool hpNamed = v.GetType() == typeof(DynamicVar) && v.Name.Contains("hp", StringComparison.OrdinalIgnoreCase) && Owner(v) is EventModel;
        if (!hpNamed && v is not (DamageVar or BlockVar or HealVar or HpLossVar or MaxHpVar)) return false;
        if (v.BaseValue <= 0 || v.BaseValue >= FmRules.FlatLimit) return false;
        var owner = Owner(v);
        string table = owner switch { RelicModel => "relics", PotionModel => "potions", EventModel => "events", _ => "" };
        if (table == "") return false;
        if (owner is EventModel) return true;
        try
        {
            var raw = LocManager.Instance.GetTable(table).GetRawText(owner!.Id.Entry + ".description");
            int i = raw.IndexOf("{" + v.Name, StringComparison.Ordinal);
            if (i >= 0)
            {
                int close = raw.IndexOf('}', i);
                if (close >= 0 && close + 1 < raw.Length && raw[close + 1] == '%') return false;
            }
        }
        catch { }
        return true;
    }
}

[HarmonyPatch(typeof(DynamicVar), "GetBaseValueForIConvertible")]
static class VarValuePatch
{
    static void Postfix(DynamicVar __instance, ref decimal __result)
    {
        if (FmItemText.Scales(__instance)) __result *= FmRules.EnemyScale;
    }
}

[HarmonyPatch(typeof(DynamicVar), nameof(DynamicVar.ToHighlightedString))]
static class VarHighlightPatch
{
    static bool Prefix(DynamicVar __instance, ref string __result)
    {
        if (!FmItemText.Scales(__instance)) return true;
        __result = ((int)(__instance.PreviewValue * FmRules.EnemyScale)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return false;
    }
}

[HarmonyPatch(typeof(DynamicVar), nameof(DynamicVar.ToString), new Type[0])]
static class VarToStringPatch
{
    static void Postfix(DynamicVar __instance, ref string __result)
    {
        if (FmItemText.Scales(__instance)) __result = (__instance.IntValue * FmRules.EnemyScale).ToString();
    }
}
