import os
H = os.path.expanduser('~/fmsts')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    ("    private bool IsMineOrOsty(Creature? c) => c != null && (c == Owner.Creature || (c.Monster is Osty && c.PetOwner == Owner));",
     "    private bool IsMineOrOsty(Creature? c) => c != null && (c == Owner.Creature || (c.PetOwner == Owner && (c.Monster is Osty || c.Monster is FmZonePet)));\n"
     "    private static bool IsZone(Creature c) => c.Monster is Osty || c.Monster is FmZonePet;"),
    ("        if (target.Monster is Osty && !(enemyHit && props.IsPoweredAttack())) return 0;",
     "        if (IsZone(target) && !(enemyHit && props.IsPoweredAttack())) return 0;"),
    # hits snapshot the front zone
    ("    private readonly Queue<(Creature dealer, FmPosition pos, int atk, int block, decimal amount)> _hits = new();",
     "    private readonly Queue<(Creature dealer, FmSlot zone, FmPosition pos, int atk, int block, decimal amount)> _hits = new();\n\n"
     "    // Enemy attacks on the duelist land on the front monster zone (zone 1, then 2, then 3).\n"
     "    public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props, Creature? dealer)\n"
     "    {\n"
     "        if (dealer == null || !dealer.IsEnemy || !props.IsPoweredAttack()) return target;\n"
     "        if (target != Owner.Creature && !(IsZone(target) && target.PetOwner == Owner)) return target;\n"
     "        return FmField.Front(Owner)?.Creature(Owner) ?? target;\n"
     "    }"),
    ("        if (dealer != null && dealer.IsEnemy && props.IsPoweredAttack() && IsMineOrOsty(target) && FmField.HasMonster(Owner))\n"
     "            _hits.Enqueue((dealer, FmField.Position, FmField.Atk, Owner.Creature.Block, amount));",
     "        if (dealer != null && dealer.IsEnemy && props.IsPoweredAttack() && IsMineOrOsty(target) && FmField.Front(Owner) is { } front)\n"
     "            _hits.Enqueue((dealer, front, front.Position, front.Atk, Owner.Creature.Block, amount));"),
    ("            if (foe.IsAlive) await HitBack(ctx, foe, h.atk * FmBonus.CounterMultiplier(Owner));\n        }\n        else if (hit <= h.block && h.block - hit > 0 && foe.IsAlive)\n        {\n            await HitBack(ctx, foe, h.block - hit);\n        }",
     "            if (foe.IsAlive) await HitBack(ctx, foe, h.atk * FmBonus.CounterMultiplier(Owner), h.zone);\n        }\n        else if (hit <= h.block && h.block - hit > 0 && foe.IsAlive)\n        {\n            await HitBack(ctx, foe, h.block - hit, h.zone);\n        }"),
    ("    private async Task HitBack(PlayerChoiceContext ctx, Creature foe, int amount)",
     "    private async Task HitBack(PlayerChoiceContext ctx, Creature foe, int amount, FmSlot zone)"),
    ("        await CreatureCmd.Damage(ctx, foe, amount, ValueProp.Unpowered, Owner.Creature, FmField.SourceCard);",
     "        var src = zone.SourceCard ?? FmField.Slots.Select(z => z.SourceCard).FirstOrDefault(c => c != null);\n"
     "        if (src == null) await FmRules.Raw(() => CreatureCmd.Damage(ctx, foe, amount, ValueProp.Unpowered, Owner.Creature, null));\n"
     "        else await CreatureCmd.Damage(ctx, foe, amount, ValueProp.Unpowered, Owner.Creature, src);"),
    ("            FmField.AttackedThisTurn = false;\n", "            foreach (var z in FmField.Slots) z.AttackedThisTurn = false;\n"),
    ("        if (creature.Monster is Osty && creature.PetOwner == Owner) await FmField.OnMonsterDestroyed(ctx, Owner);",
     "        if (creature.PetOwner == Owner && FmField.SlotOf(creature) is { } zone) await FmField.OnMonsterDestroyed(ctx, Owner, zone);"),
])
print('ok')
