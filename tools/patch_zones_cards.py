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

edit('src/FMDuelist/Cards/FmPositionCard.cs', [(
    "public sealed class FmDefensePosition : FmPositionCard { public FmDefensePosition() : base(FmPosition.Defense) { } }",
    "public sealed class FmDefensePosition : FmPositionCard { public FmDefensePosition() : base(FmPosition.Defense) { } }\n\n"
    "/// <summary>An empty monster zone in the zone-choice window.</summary>\n"
    "public sealed class FmEmptyZone : FmPositionCard\n{\n    public int Zone;\n    public FmEmptyZone() : base(FmPosition.Attack) { }\n}")])

edit('src/FMDuelist/Cards/FmSpellCard.cs', [
    ("    protected override bool IsPlayable => Kind != FmSpellKind.Equip || (Owner != null && FmField.HasMonster(Owner) && FmData.CanEquip(FmId, FmField.MonsterId));",
     "    protected override bool IsPlayable => Kind switch\n    {\n"
     "        FmSpellKind.Equip => Owner != null && FmField.Living(Owner).Any(s => FmData.CanEquip(FmId, s.MonsterId)),\n"
     "        FmSpellKind.Trap => FmField.CanSetTrap,\n        _ => true,\n    };"),
    ("                if (Owner.IsOstyAlive) await CreatureCmd.Kill(Owner.Osty!);",
     "                foreach (var z in FmField.Living(Owner).ToList()) if (z.Creature(Owner) is { } zc) await CreatureCmd.Kill(zc);"),
    ("                FmField.AtkBonus += Value; FmField.DefBonus += Value;\n                FmField.Equips.Add(FmId);",
     "                if (await FmFusion.ChooseEquipTarget(ctx, Owner, this, FmId) is { } eq)\n                {\n"
     "                    eq.AtkBonus += Value; eq.DefBonus += Value;\n                    eq.Equips.Add(FmId);\n                }"),
])

edit('src/FMDuelist/Cards/FmRitualCard.cs', [
    ("        var pos = await FmFusion.ChoosePosition(ctx, Owner, ResultId, this);\n        await FmField.Summon(ctx, Owner, ResultId, pos, cardPlay.Target, this);",
     "        var zone = await FmFusion.ChooseZone(ctx, Owner, this);\n"
     "        var pos = await FmFusion.ChoosePosition(ctx, Owner, ResultId, this);\n"
     "        await FmField.Summon(ctx, Owner, zone, ResultId, pos, cardPlay.Target, this);")])

edit('src/FMDuelist/FmLoc.cs', [
    ('            case FmAttackPosition: return ("Attack Position",',
     '            case FmEmptyZone ez: return ($"Empty Zone {ez.Zone + 1}", "Summon into this empty monster zone.");\n'
     '            case FmAttackPosition: return ("Attack Position",'),
    ("foreach (var t in FmSheet.PoolTypes.Concat(FmSheet.ColorlessTypes).Append(typeof(FmAttackPosition))",
     "foreach (var t in FmSheet.PoolTypes.Concat(FmSheet.ColorlessTypes).Append(typeof(FmEmptyZone)).Append(typeof(FmAttackPosition))"),
])
print('ok')
