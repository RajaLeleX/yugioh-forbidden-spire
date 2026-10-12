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

# sheet: act 1 dragons only through the 20% roll; acts 2-3 looser
edit('tools/make_sheets.py', [(
    "        acts=['Rare','Uncommon','Common'] if lv<=4 else ['None','Rare','Uncommon']",
    "        acts=['None','Common','Common'] if lv<=4 else ['None','Uncommon','Common']")])
edit('tools/make_sheets.py', [(
    "w('starter_random',",
    "w('dragon_roll',[dict(id='act1_weak_dragon', act=1, chance=0.20, maxLevel=4)])\nw('starter_random',")])

edit('tools/codegen.py', [(
    "out.append('    public static readonly Type[] DragonBuffCards",
    "DR = json.load(open(f'{H}/sheets/dragon_roll.json'))[0]\n"
    "out.append(f\"    public const double Act1DragonChance = {DR['chance']};\")\n"
    "out.append('    public static readonly int[] WeakDragons = { ' + ', '.join(str(m['id']) for m in mons if m['dragonTier'] == 'weak' and not m.get('exodia')) + ' };')\n"
    "out.append('    public static readonly Type[] DragonBuffCards")])

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [(
    "    // --- Skip really skips:",
    "    // --- Act 1: each card reward has a 20% chance to offer a weak Dragon ---\n"
    "    public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)\n"
    "    {\n"
    "        if (!Mine(player) || player.RunState.CurrentActIndex != 0 || options.Count == 0 || creationOptions.CustomCardPool != null) return false;\n"
    "        if (!creationOptions.CardPools.Any(p => p is FmCardPool)) return false;\n"
    "        var rng = player.PlayerRng.Rewards;\n"
    "        if (rng.NextFloat() >= FmSheet.Act1DragonChance) return false;\n"
    "        int id = FmSheet.WeakDragons[rng.NextInt(0, FmSheet.WeakDragons.Length)];\n"
    "        var dragon = player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player);\n"
    "        options[rng.NextInt(0, options.Count)].ModifyCard(dragon);\n"
    "        return true;\n"
    "    }\n\n"
    "    // --- Skip really skips:")])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'dragon_rewards' for r in rows):
    rows.append({"id": "dragon_rewards", "what": "Act 1: 20% per card reward to swap in a random weak Dragon (lv<=4); Act 2: weak Common, strong Uncommon; Act 3: all Dragons Common", "value": 0.2, "hook": "card_pool"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
