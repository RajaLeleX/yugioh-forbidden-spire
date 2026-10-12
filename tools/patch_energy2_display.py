import os, json, subprocess
H = os.path.expanduser('~/fmsts')

E = json.load(open(f'{H}/sheets/removed_energy.json'))
more_r = ["Bookmark", "BrilliantScarf", "ChemicalX", "FakeSneckoEye", "IceCream", "IntimidatingHelmet", "MummifiedHand", "PowerCell", "RadiantPearl", "SneckoEye", "BoundPhylactery"]
more_p = ["SneckoOil", "TouchOfInsanity"]
E['relics'] = sorted(set(E['relics'] + more_r))
E['potions'] = sorted(set(E['potions'] + more_p))
E['note'] = "Base-game relics and potions that touch energy at all (gain, max, costs, unspent energy, Snecko randomizing). FM cards cost 0, so these are removed from FM runs."
json.dump(E, open(f'{H}/sheets/removed_energy.json', 'w'), indent=1)
subprocess.run(['python', f'{H}/tools/patch_energy.py'], check=True)

open(f'{H}/src/FMDuelist/FmItemText.cs', 'w').write('''using FMDuelist.Game;
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
        if (v is not (DamageVar or BlockVar or HealVar or HpLossVar or MaxHpVar)) return false;
        if (v.BaseValue <= 0 || v.BaseValue >= FmRules.FlatLimit) return false;
        var owner = Owner(v);
        string table = owner switch { RelicModel => "relics", PotionModel => "potions", _ => "" };
        if (table == "") return false;
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

[HarmonyPatch(typeof(DynamicVar), nameof(DynamicVar.ToString))]
static class VarToStringPatch
{
    static void Postfix(DynamicVar __instance, ref string __result)
    {
        if (FmItemText.Scales(__instance)) __result = (__instance.IntValue * FmRules.EnemyScale).ToString();
    }
}
''')

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'item_text_x100' for r in rows):
    rows.append({"id": "item_text_x100", "what": "Relic/potion text shows Damage/Block/Heal/HpLoss/MaxHp numbers (<200, not percentages) at x100 in FM runs", "value": 100, "hook": "enemy_damage_scale"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok', len(E['relics']), len(E['potions']))
