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

# sheet: Exodia pieces are never offered (rewards/shops); only the Exodia Card Box gives them
edit('tools/make_sheets.py', [
    ("DRAGON_BUFF_CARDS=[4,200]", "DRAGON_BUFF_CARDS=[4,200]\nEXODIA=[21,19,20,17,18]"),
    ("    mons.append(dict(id=c['id'],cls=f\"FmCard{c['id']:03d}\",type=c['type'],level=lv,atk=c['atk'],def_=c['def_'],",
     "    if c['id'] in EXODIA: acts=['None','None','None']\n"
     "    mons.append(dict(id=c['id'],cls=f\"FmCard{c['id']:03d}\",type=c['type'],level=lv,atk=c['atk'],def_=c['def_'],exodia=c['id'] in EXODIA,"),
])

# relic: Exodia Card Box (Neow option), adds the five pieces
edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    ("/// <summary>Run-start blessing: dragon fusions of level 5+ are allowed; adds Baby Dragon and Petit Dragon.</summary>",
     "/// <summary>Run-start option: adds all five pieces of Exodia the Forbidden One to the deck.</summary>\n"
     "public sealed class ExodiaCardBox : RelicModel\n{\n"
     "    public override RelicRarity Rarity => RelicRarity.Ancient;\n"
     "    public override bool IsAllowedInShops => false;\n\n"
     "    public override async Task AfterObtained()\n    {\n"
     "        foreach (var id in FmExodia.Pieces)\n        {\n"
     "            var card = Owner.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), Owner);\n"
     "            await CardPileCmd.Add(card, PileType.Deck);\n        }\n    }\n}\n\n"
     "/// <summary>Run-start blessing: dragon fusions of level 5+ are allowed; adds Baby Dragon and Petit Dragon.</summary>"),
    # win check whenever the hand changes
    ("    // --- FM hand: draw up to 5, never discard the hand ---",
     "    // --- Exodia: five pieces in hand win the duel ---\n"
     "    public override async Task AfterCardDrawn(PlayerChoiceContext ctx, CardModel card, bool fromHandDraw)\n"
     "    {\n        if (card.Owner == Owner && card is FmCardBase f && FmExodia.Pieces.Contains(f.FmId)) await FmExodia.CheckAndObliterate(Owner);\n    }\n\n"
     "    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)\n"
     "    {\n        if (card.Owner == Owner && card is FmCardBase f && FmExodia.Pieces.Contains(f.FmId) && card.Pile?.Type == PileType.Hand) await FmExodia.CheckAndObliterate(Owner);\n    }\n\n"
     "    // Exodia pieces are never sold\n"
     "    public override IEnumerable<CardModel> ModifyMerchantCardPool(Player player, IEnumerable<CardModel> options)\n"
     "        => !Mine(player) ? options : options.Where(c => !(c is FmCardBase f && FmExodia.Pieces.Contains(f.FmId))).ToList();\n\n"
     "    // --- FM hand: draw up to 5, never discard the hand ---"),
])

# Neow offers it; pool registration; loc; icon art
edit('src/FMDuelist/FmMod.cs', [
    ("            __result = __result.Append(opt).ToList();",
     "            var exo = (EventOption)RelicOption.Invoke(__instance, new object?[] { ModelDb.Relic<ExodiaCardBox>().ToMutable(), \"INITIAL\", \"NEOW.pages.DONE.POSITIVE.description\" })!;\n"
     "            __result = __result.Append(opt).Append(exo).ToList();"),
    ("        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, DragonTreasure>();\n",
     "        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, DragonTreasure>();\n"
     "        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, ExodiaCardBox>();\n"),
    ("FmRelic f => f.ArtId, _ => null };", "ExodiaCardBox => 21, FmRelic f => f.ArtId, _ => null };"),
])
edit('src/FMDuelist/FmLoc.cs', [(
    '                [E(typeof(DragonTreasure)) + ".title"] = "Dragon Treasure",',
    '                [E(typeof(ExodiaCardBox)) + ".title"] = "Exodia Card Box",\n'
    '                [E(typeof(ExodiaCardBox)) + ".description"] = "Add all five pieces of Exodia the Forbidden One to your deck. Hold all five in your hand to obliterate every enemy and win the duel.",\n'
    '                [E(typeof(ExodiaCardBox)) + ".flavor"] = "Sealed away, piece by piece.",\n'
    '                [E(typeof(DragonTreasure)) + ".title"] = "Dragon Treasure",')])
# monster card text for the pieces
edit('src/FMDuelist/FmLoc.cs', [(
    '            case FmMonsterCard: return (info.Name, Stats(m.FmId) + "\\nSummon (1 per turn). Right-click to fuse.");',
    '            case FmMonsterCard when FmExodia.Pieces.Contains(m.FmId): return (info.Name, Stats(m.FmId) + "\\nHold all 5 pieces of the Forbidden One in your hand: obliterate every enemy.");\n'
    '            case FmMonsterCard: return (info.Name, Stats(m.FmId) + "\\nSummon (1 per turn). Right-click to fuse.");')])
edit('src/FMDuelist/FmLoc.cs', [("using FMDuelist.Data;", "using FMDuelist.Data;\nusing FMDuelist.Game;")])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in [{"id": "exodia", "what": "Neow option Exodia Card Box adds the 5 Forbidden One pieces (17-21). All 5 in hand: animation, every enemy killed. Pieces never in rewards or shops", "value": 5, "hook": "monster_play"}]:
    if not any(x['id'] == r['id'] for x in rows): rows.append(r)
json.dump(rows, open(sy, 'w'), indent=1)

pf = f'{H}/tools/preflight.py'
t = open(pf).read()
if 'exodia' not in t:
    t = t.replace("print('rows:", "for m in mons:\n    if m.get('exodia') and (m['act1'], m['act2'], m['act3']) != ('None', 'None', 'None'): problems.append(f\"exodia piece {m['id']} is offered as a reward\")\nprint('rows:", 1)
    open(pf, 'w').write(t)
print('ok')
