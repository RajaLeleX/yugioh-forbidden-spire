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

# flat (base-game sized) amounts get x100; percentage-based amounts computed from 8000 LP are already big
edit('src/FMDuelist/Game/FmRules.cs', [(
    "    public static int RawDepth;",
    "    public static int RawDepth;\n\n"
    "    /// <summary>Base-game flat numbers (HP loss, heals, max HP) are small; anything this size or bigger was\n"
    "    /// already computed from FM-scale values (e.g. 30% of 8000 LP) and must not be scaled again.</summary>\n"
    "    public const decimal FlatLimit = 200m;")])

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [(
    "        if (dealer == null && target != null && (target.IsEnemy || IsMineOrOsty(target))) return FmRules.EnemyScale; // poison, status cards",
    "        if (dealer == null && target != null && (target.IsEnemy || IsMineOrOsty(target)) && amount < FmRules.FlatLimit) return FmRules.EnemyScale; // poison, status cards, event HP costs")])

edit('src/FMDuelist/Cards/FmSpellCard.cs', [
    ("                await CreatureCmd.Heal(me, Value * Relics.FmBonus.HealMultiplier(Owner));",
     "                await FmRules.Raw(() => CreatureCmd.Heal(me, Value * Relics.FmBonus.HealMultiplier(Owner)));"),
    ("                if (Kind == FmSpellKind.BlockHeal) await CreatureCmd.Heal(me, Value);",
     "                if (Kind == FmSpellKind.BlockHeal) await FmRules.Raw(() => CreatureCmd.Heal(me, Value * Relics.FmBonus.HealMultiplier(Owner)));"),
])
edit('src/FMDuelist/Relics/FmRelic.cs', [
    ("        await CreatureCmd.Heal(Owner.Creature, Value);", "        await FmRules.Raw(() => CreatureCmd.Heal(Owner.Creature, Value));")])

open(f'{H}/src/FMDuelist/FmBalance.cs', 'w').write('''using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace FMDuelist;

/// <summary>
/// In FM runs, base-game flat heals and max-HP changes (relics, events, potions, Neow, enemy regeneration) are x100
/// so they match 8000 LP and x100 enemies. FM's own effects run inside FmRules.Raw and keep their FM numbers.
/// </summary>
static class FmBalance
{
    public static bool Scales(Creature c, decimal amount) =>
        FmRules.RunActive && FmRules.RawDepth == 0 && amount > 0 && amount < FmRules.FlatLimit && c.Monster is not Osty;
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
static class HealScalePatch
{
    static void Prefix(Creature creature, ref decimal amount)
    {
        if (FmBalance.Scales(creature, amount)) amount *= FmRules.EnemyScale;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp))]
static class GainMaxHpScalePatch
{
    static void Prefix(Creature creature, ref decimal amount)
    {
        if (FmBalance.Scales(creature, amount)) amount *= FmRules.EnemyScale;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseMaxHp))]
static class LoseMaxHpScalePatch
{
    static void Prefix(Creature creature, ref decimal amount)
    {
        if (FmBalance.Scales(creature, amount)) amount *= FmRules.EnemyScale;
    }
}
''')

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'scale_flat_hp' for r in rows):
    rows.append({"id": "scale_flat_hp", "what": "FM runs: base-game flat HP loss, heals and max-HP changes under 200 are x100 (events, relics, potions, Neow, enemy regen); FM effects are raw", "value": 200, "hook": "enemy_damage_scale"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
