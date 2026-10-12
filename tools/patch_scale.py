import os, json
H = os.path.expanduser('~/fmsts')

def edit(path, pairs, count=1):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b) if count == 0 else s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# FmRaw: damage/block the mod already expresses in FM numbers must not be scaled again
edit('src/FMDuelist/Game/FmRules.cs', [(
    "    public static bool RunActive;",
    "    public static bool RunActive;\n\n"
    "    /// <summary>While > 0, damage/block being dealt is already in FM numbers (not multiplied by 100).</summary>\n"
    "    public static int RawDepth;\n"
    "    public static async Task Raw(Func<Task> f) { RawDepth++; try { await f(); } finally { RawDepth--; } }")])

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    ("""        if (dealer != null && dealer.IsEnemy) return FmRules.EnemyScale;
        if (dealer != null && IsMineOrOsty(dealer) && !FromFm(cardSource) && target != null && target.IsEnemy) return FmRules.EnemyScale;
        return 1m;""",
     """        if (FmRules.RawDepth > 0 || FromFm(cardSource)) return 1m;
        if (dealer != null && dealer.IsEnemy) return FmRules.EnemyScale;
        if (dealer != null && IsMineOrOsty(dealer) && target != null && target.IsEnemy) return FmRules.EnemyScale;
        if (dealer == null && target != null && (target.IsEnemy || IsMineOrOsty(target))) return FmRules.EnemyScale; // poison, status cards
        return 1m;"""),
    ("""        if (target.IsEnemy) return FmRules.EnemyScale;
        if (target == Owner.Creature && cardSource != null && !FromFm(cardSource)) return FmRules.EnemyScale;
        return 1m;""",
     """        if (target.IsEnemy) return FmRules.EnemyScale;
        if (FmRules.RawDepth > 0 || FromFm(cardSource)) return 1m;
        if (target == Owner.Creature) return FmRules.EnemyScale; // relics, powers, potions, colorless/base cards
        return 1m;"""),
    ("                        case FmTrapKind.DestroyLe: await CreatureCmd.Damage(ctx, attacker, trap.value, ValueProp.Unpowered, Owner.Creature, null); break;",
     "                        case FmTrapKind.DestroyLe: await FmRules.Raw(() => CreatureCmd.Damage(ctx, attacker, trap.value, ValueProp.Unpowered, Owner.Creature, null)); break;"),
    ("                        case FmTrapKind.DestroyAny: await CreatureCmd.Damage(ctx, attacker, 3000, ValueProp.Unpowered, Owner.Creature, null); break;",
     "                        case FmTrapKind.DestroyAny: await FmRules.Raw(() => CreatureCmd.Damage(ctx, attacker, 3000, ValueProp.Unpowered, Owner.Creature, null)); break;"),
    ("                        case FmTrapKind.ReflectBurn: await CreatureCmd.Damage(ctx, attacker, sprungHit, ValueProp.Unpowered, Owner.Creature, null); break;",
     "                        case FmTrapKind.ReflectBurn: await FmRules.Raw(() => CreatureCmd.Damage(ctx, attacker, sprungHit, ValueProp.Unpowered, Owner.Creature, null)); break;"),
    ("                await CreatureCmd.Damage(ctx, Owner.Creature, excess, ValueProp.Unblockable | ValueProp.Unpowered, null, null);",
     "                await FmRules.Raw(() => CreatureCmd.Damage(ctx, Owner.Creature, excess, ValueProp.Unblockable | ValueProp.Unpowered, null, null));"),
])

edit('src/FMDuelist/Game/FmField.cs', [
    ("            await CreatureCmd.Damage(ctx, player.Creature, Relics.FmBonus.ExtraSummonCost(player), ValueProp.Unblockable | ValueProp.Unpowered, null, null);",
     "            await FmRules.Raw(() => CreatureCmd.Damage(ctx, player.Creature, Relics.FmBonus.ExtraSummonCost(player), ValueProp.Unblockable | ValueProp.Unpowered, null, null));"),
    ("            await CreatureCmd.GainBlock(osty, Def, ValueProp.Unpowered, null);",
     "            await FmRules.Raw(() => CreatureCmd.GainBlock(osty, Def, ValueProp.Unpowered, null));"),
])

edit('src/FMDuelist/Relics/FmRelic.cs', [
    ("            await CreatureCmd.GainBlock(player.Creature, Value, ValueProp.Unpowered, null);",
     "            await FmRules.Raw(() => CreatureCmd.GainBlock(player.Creature, Value, ValueProp.Unpowered, null));"),
])

# enemy power numbers that are damage/block amounts display in FM scale
open(f'{H}/src/FMDuelist/FmPowerDisplay.cs', 'w').write('''using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace FMDuelist;

/// <summary>Enemy powers whose number is damage or Block (Strength, Thorns, Poison...) show it in FM numbers (x100), matching what they really do.</summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.DisplayAmount), MethodType.Getter)]
static class PowerDisplayPatch
{
    static readonly HashSet<Type> Scaled = new()
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
''')

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'scale_everything' for r in rows):
    rows.append({"id": "scale_everything", "what": "Every non-FM damage/Block number is x100 (enemy attacks, thorns, poison, statuses, base relics/potions/powers on the duelist); FM effects use raw FM numbers; enemy Strength/Dexterity/Thorns/Poison/Constrict/BlockNextTurn display x100", "value": 100, "hook": "enemy_damage_scale"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
