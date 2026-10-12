import os, json
H = os.path.expanduser('~/fmsts')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# 1) enemy power tooltips: "{Amount}" shows the x100 number for damage/Block powers (same list as the icon numbers)
edit('src/FMDuelist/FmPowerDisplay.cs', [
    ("[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.DisplayAmount), MethodType.Getter)]\nstatic class PowerDisplayPatch\n{\n    static readonly HashSet<Type> Scaled = new()",
     "[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.DisplayAmount), MethodType.Getter)]\nstatic class PowerDisplayPatch\n{\n    public static bool ScalesFor(PowerModel p) =>\n        FmRules.RunActive && p.Owner != null && Scaled.Contains(p.GetType()) && (p.Owner.IsEnemy || p is PoisonPower);\n\n    public static readonly HashSet<Type> Scaled = new()"),
])
open(f'{H}/src/FMDuelist/FmPowerText.cs', 'w').write('''using FMDuelist.Game;
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
''')

# 2) relic/potion/event text: event numbers too (HP costs, heals, damage, max HP)
edit('src/FMDuelist/FmItemText.cs', [
    ('        string table = owner switch { RelicModel => "relics", PotionModel => "potions", _ => "" };\n        if (table == "") return false;',
     '        string table = owner switch { RelicModel => "relics", PotionModel => "potions", EventModel => "events", _ => "" };\n        if (table == "") return false;\n        if (owner is EventModel) return true;'),
])

# 3) base-game card previews with no target yet still show x100 damage
edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [(
    "        if (dealer != null && IsMineOrOsty(dealer) && target != null && target.IsEnemy) return FmRules.EnemyScale;",
    "        if (dealer != null && IsMineOrOsty(dealer) && (target == null || target.IsEnemy)) return FmRules.EnemyScale;")])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in rows:
    if r['id'] == 'item_text_x100':
        r['what'] = 'In FM runs all text matches the x100 effects: relic/potion/event numbers (<200, not %), enemy power tooltips and icons, base-card damage/Block previews'
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
