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

# sheet rows: pool column; six colorless support spells; rituals become colorless
edit('tools/make_sheets.py', [
    (" (664,'block_heal',500,'Self','Common',0),(663,'block',1500,'Self','Uncommon',0),",
     " (664,'block_heal',500,'Self','Common',0),(663,'block',1500,'Self','Uncommon',0),\n"
     " (329,'stun',1,'AnyEnemy','Uncommon',0,'colorless'),(653,'burn',1500,'AnyEnemy','Uncommon',0,'colorless'),\n"
     " (660,'burn_all',1200,'AllEnemies','Uncommon',0,'colorless'),(661,'burn_strongest',2000,'AllEnemies','Rare',0,'colorless'),\n"
     " (662,'weak_all',3,'AllEnemies','Uncommon',0,'colorless'),(656,'strip_block_burn_all',1000,'AllEnemies','Rare',0,'colorless'),"),
    ("for cid,kind,val,tgt,rar,st in S:\n    spells.append(dict(id=cid,cls=f\"FmCard{cid:03d}\",kind=kind,value=val,target=tgt,rarity=rar,starter=st,",
     "for row in S:\n    cid,kind,val,tgt,rar,st=row[:6]; pool=row[6] if len(row)>6 else 'fm'\n    spells.append(dict(id=cid,cls=f\"FmCard{cid:03d}\",kind=kind,value=val,target=tgt,rarity=rar,starter=st,pool=pool,"),
    ("    rits.append(dict(id=cid,cls=f\"FmCard{cid:03d}\",resultId=rid,materials=3,rarity='Rare',",
     "    rits.append(dict(id=cid,cls=f\"FmCard{cid:03d}\",resultId=rid,materials=3,rarity='Rare',pool='colorless',"),
])

edit('tools/codegen.py', [
    ("'block_heal': 'BlockHeal'}", "'block_heal': 'BlockHeal', 'burn_strongest': 'BurnStrongest', 'weak_all': 'WeakAll', 'strip_block_burn_all': 'StripBlockAll'}"),
    ("out.append('    public static readonly Type[] PoolTypes = { ' + ', '.join(f\"typeof({r['cls']})\" for r in allrows) + ' };')",
     "out.append('    public static readonly Type[] PoolTypes = { ' + ', '.join(f\"typeof({r['cls']})\" for r in allrows if r.get('pool', 'fm') == 'fm') + ' };')\n"
     "out.append('    public static readonly Type[] ColorlessTypes = { ' + ', '.join(f\"typeof({r['cls']})\" for r in allrows if r.get('pool', 'fm') == 'colorless') + ' };')\n"
     "out.append('    public static readonly HashSet<int> ColorlessIds = new() { ' + ', '.join(str(r['id']) for r in allrows if r.get('pool', 'fm') == 'colorless') + ' };')"),
])

edit('tools/preflight.py', [
    ("'block', 'block_weak',", "'burn_strongest', 'weak_all', 'strip_block_burn_all', 'block', 'block_weak',")])

edit('src/FMDuelist/Cards/FmSpellCard.cs', [
    ("Block, BlockWeak, BlockCleanse, BlockHeal }", "Block, BlockWeak, BlockCleanse, BlockHeal, BurnStrongest, WeakAll, StripBlockAll }"),
    ("            case FmSpellKind.Block:\n",
     "            case FmSpellKind.BurnStrongest:\n"
     "                if (enemies.OrderByDescending(e => e.CurrentHp).FirstOrDefault() is { } big) await CreatureCmd.Damage(ctx, big, Value, ValueProp.Unpowered, me, this);\n"
     "                break;\n"
     "            case FmSpellKind.WeakAll:\n"
     "                await PowerCmd.Apply<WeakPower>(ctx, enemies, Value, me, this);\n"
     "                break;\n"
     "            case FmSpellKind.StripBlockAll:\n"
     "                foreach (var e in enemies) if (e.Block > 0) await CreatureCmd.LoseBlock(e, e.Block);\n"
     "                await CreatureCmd.Damage(ctx, enemies, Value, ValueProp.Unpowered, me, this);\n"
     "                break;\n"
     "            case FmSpellKind.Block:\n"),
    ("kind is FmSpellKind.Burn or FmSpellKind.BurnSelfDmg or FmSpellKind.BurnAll or FmSpellKind.DarkHole ? CardType.Attack",
     "kind is FmSpellKind.Burn or FmSpellKind.BurnSelfDmg or FmSpellKind.BurnAll or FmSpellKind.DarkHole or FmSpellKind.BurnStrongest or FmSpellKind.StripBlockAll ? CardType.Attack"),
])

edit('src/FMDuelist/Cards/FmCardBase.cs', [
    ("    public override CardPoolModel Pool => ModelDb.CardPool<FmCardPool>();",
     "    public override CardPoolModel Pool => FmSheet.ColorlessIds.Contains(FmId) && this is not FmPositionCard\n"
     "        ? ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>() : ModelDb.CardPool<FmCardPool>();")])

edit('src/FMDuelist/FmLoc.cs', [
    ("                FmSpellKind.VulnerableAll =>",
     "                FmSpellKind.BurnStrongest => $\"Deal {s.Value} damage to the enemy with the most HP.\",\n"
     "                FmSpellKind.WeakAll => $\"Apply {s.Value} Weak to ALL enemies.\",\n"
     "                FmSpellKind.StripBlockAll => $\"Remove all Block from ALL enemies, then deal {s.Value} damage to ALL enemies.\",\n"
     "                FmSpellKind.VulnerableAll =>"),
    ("foreach (var t in FmSheet.PoolTypes.Append(", "foreach (var t in FmSheet.PoolTypes.Concat(FmSheet.ColorlessTypes).Append("),
])

edit('src/FMDuelist/FmMod.cs', [
    ("        FmTest.Start();",
     "        ModHelper.SubscribeForRunStateHooks(\"FMDuelist\", rs => { FmRules.RunActive = rs.Players.Any(FmRules.IsFmPlayer); return Array.Empty<AbstractModel>(); });\n"
     "        FmTest.Start();"),
])

open(f'{H}/src/FMDuelist/FmColorless.cs', 'w').write('''using FMDuelist.Cards;
using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace FMDuelist;

/// <summary>While an FM duelist is in the run, the colorless pool is FM's neutral cards (rituals and support magic).</summary>
[HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.AllCards), MethodType.Getter)]
static class ColorlessPoolPatch
{
    static CardModel[]? _fm;

    static void Postfix(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        if (__instance is not ColorlessCardPool || !FmRules.RunActive) return;
        _fm ??= FmSheet.ColorlessTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t))).ToArray();
        __result = _fm;
    }
}
''')

edit('src/FMDuelist/Game/FmRules.cs', [
    ("    public static int ActIndex;", "    public static int ActIndex;\n\n    /// <summary>True while the current run has an FM duelist (set from the run hook subscription).</summary>\n    public static bool RunActive;")])
print('ok')
