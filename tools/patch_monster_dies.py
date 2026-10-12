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

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    # powered enemy hits on the duelist pass through so the game redirects them to the monster (1 HP: any hit destroys it)
    ("        if (target == Owner.Creature && enemyHit && FmField.HasMonster(Owner)) return 0;\n        return amount;\n    }\n\n    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)\n        => FmField.SwordsTurns > 0 && IsMineOrOsty(target) ? 0 : amount;",
     "        if (target == Owner.Creature && enemyHit && FmField.HasMonster(Owner) && !props.IsPoweredAttack()) return 0;\n        return amount;\n    }\n\n"
     "    // Overkill from the monster spills onto the duelist in the base game; in a duel the duelist only takes the ATK excess.\n"
     "    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)\n"
     "    {\n        if (FmField.SwordsTurns > 0 && IsMineOrOsty(target)) return 0;\n"
     "        if (target == Owner.Creature && dealer != null && dealer.IsEnemy && _hits.Count > 0) return 0;\n        return amount;\n    }"),
    # block lives on the duelist (the game blocks hits on Osty with its owner's Block)
    ("            _hits.Enqueue((dealer, FmField.Position, FmField.Atk, Owner.Osty!.Block, amount));",
     "            _hits.Enqueue((dealer, FmField.Position, FmField.Atk, Owner.Creature.Block, amount));"),
])

edit('src/FMDuelist/Game/FmField.cs', [
    ("            await FmRules.Raw(() => CreatureCmd.GainBlock(osty, Def, ValueProp.Unpowered, null));",
     "            await FmRules.Raw(() => CreatureCmd.GainBlock(player.Creature, Def, ValueProp.Unpowered, null));"),
])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in rows:
    if r['id'] == 'defense_block':
        r['what'] = 'Defense position at end of your turn: the duelist gains Block = DEF (the game blocks hits on Osty with its owner\'s Block)'
    if r['id'] == 'osty_is_monster':
        r['what'] = 'Osty becomes the summoned FM monster with 1 HP; any enemy hit that gets past Block destroys it, whatever its ATK'
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
