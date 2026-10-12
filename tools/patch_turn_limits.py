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

# a monster that attacked this turn stays in Attack position
edit('src/FMDuelist/Game/FmField.cs', [
    ("        if (slot.MonsterId == 0) return;\n        slot.Position = slot.Position == FmPosition.Attack ? FmPosition.Defense : FmPosition.Attack;",
     "        if (slot.MonsterId == 0) return;\n        if (slot.Position == FmPosition.Attack && slot.AttackedThisTurn) return; // attacked: can't go to Defense this turn\n"
     "        slot.Position = slot.Position == FmPosition.Attack ? FmPosition.Defense : FmPosition.Attack;"),
    ("    public static int SummonsThisTurn, SummonsThisCombat;",
     "    public static int SummonsThisTurn, SummonsThisCombat;\n    /// <summary>Raigeki or Dark Hole was played this turn (only one of them per turn).</summary>\n    public static bool MassSpellThisTurn;"),
    ("        SummonedThisTurn = false; SummonsThisTurn = 0; SummonsThisCombat = 0;",
     "        SummonedThisTurn = false; SummonsThisTurn = 0; SummonsThisCombat = 0; MassSpellThisTurn = false;"),
])
edit('src/FMDuelist/Game/FmUi.cs', [
    ('_slot.AttackedThisTurn ? "ATK  (attacked)"', '_slot.AttackedThisTurn ? "ATK  (attacked - locked)"'),
])
edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    ("            FmField.SummonsThisTurn = 0;\n", "            FmField.SummonsThisTurn = 0;\n            FmField.MassSpellThisTurn = false;\n"),
])
edit('src/FMDuelist/Cards/FmSpellCard.cs', [
    ("        FmSpellKind.Trap => FmField.CanSetTrap,\n        _ => true,",
     "        FmSpellKind.Trap => FmField.CanSetTrap,\n        _ when IsMassSpell => !FmField.MassSpellThisTurn,\n        _ => true,"),
    ("    protected override bool ShouldGlowGoldInternal",
     "    /// <summary>Raigeki (337) and Dark Hole (336): only one of them per turn.</summary>\n    public bool IsMassSpell => FmId is 336 or 337;\n\n    protected override bool ShouldGlowGoldInternal"),
    ("        var enemies = combat.HittableEnemies.Where(e => e.IsAlive).ToList();\n",
     "        var enemies = combat.HittableEnemies.Where(e => e.IsAlive).ToList();\n        if (IsMassSpell) { FmField.MassSpellThisTurn = true; FmField.NotifyChanged(); }\n"),
])
edit('src/FMDuelist/FmLoc.cs', [
    ('FmSpellKind.BurnAll => $"Deal {s.Value} damage to ALL enemies.",',
     'FmSpellKind.BurnAll => $"Deal {s.Value} damage to ALL enemies." + (s.IsMassSpell ? " Only one Raigeki or Dark Hole can be played per turn." : ""),'),
    ("and you can't summon any more monsters this turn.\",",
     "and you can't summon any more monsters this turn. Only one Raigeki or Dark Hole can be played per turn.\","),
    ('"Upright. At the end of each of your turns it attacks for its ATK. Attacked: hits back for its ATK."',
     '"Upright. At the end of each of your turns it attacks for its ATK. Attacked: hits back for its ATK. Once it has attacked, it can\'t switch to Defense that turn."'),
])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in [{"id": "attack_locks_position", "what": "A monster that attacked this turn can't switch to Defense until next turn", "value": 1, "hook": "zone"},
          {"id": "one_mass_spell_per_turn", "what": "Only one Raigeki (337) or Dark Hole (336) can be played per turn", "value": 1, "hook": "zone"}]:
    if not any(x['id'] == r['id'] for x in rows): rows.append(r)
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
