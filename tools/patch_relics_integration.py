import os
H = os.path.expanduser('~/fmsts')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:70]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

edit('tools/codegen.py', [(
    "out += ['}', '']\nopen(f'{H}/src/FMDuelist/Cards/Generated.cs', 'w')",
    "out.append('    private static readonly Dictionary<int, Type> _types = new() { ' + ', '.join(f\"[{r['id']}] = typeof({r['cls']})\" for r in allrows) + ' };')\n"
    "out.append('    public static Type TypeFor(int id) => _types[id];')\n"
    "out += ['}', '']\nopen(f'{H}/src/FMDuelist/Cards/Generated.cs', 'w')")])

edit('src/FMDuelist/Game/FmField.cs', [
    ("    public static int Atk => MonsterId == 0 ? 0 : FmData.Get(MonsterId).Atk + AtkBonus + FieldBonus(MonsterId);\n"
     "    public static int Def => MonsterId == 0 ? 0 : FmData.Get(MonsterId).Def + DefBonus + FieldBonus(MonsterId);",
     "    public static readonly List<int> Equips = new();\n"
     "    public static int SummonsThisTurn, SummonsThisCombat;\n"
     "    public static int Atk => MonsterId == 0 ? 0 : FmData.Get(MonsterId).Atk + AtkBonus + FieldBonus(MonsterId) + Relics.FmBonus.TypeBonus(Duelist, MonsterId);\n"
     "    public static int Def => MonsterId == 0 ? 0 : FmData.Get(MonsterId).Def + DefBonus + FieldBonus(MonsterId) + Relics.FmBonus.TypeBonus(Duelist, MonsterId)\n"
     "        + (Position == FmPosition.Defense ? Relics.FmBonus.DefenseBonus(Duelist) : 0);\n"
     "    public static bool CanSummon(Player? p) => SummonsThisTurn < Relics.FmBonus.SummonsPerTurn(p);"),
    ("SummonedThisTurn = false; AttackedThisTurn = false;",
     "SummonedThisTurn = false; SummonsThisTurn = 0; SummonsThisCombat = 0; Equips.Clear(); AttackedThisTurn = false;"),
    ("TargetCombatId = target?.CombatId; SourceCard = source; SummonedThisTurn = true; Duelist = player;",
     "TargetCombatId = target?.CombatId; SourceCard = source; SummonedThisTurn = true; Duelist = player;\n"
     "        Equips.Clear(); Equips.AddRange(PendingEquips); PendingEquips.Clear();\n"
     "        if (SummonsThisTurn >= 1 && Relics.FmBonus.ExtraSummonCost(player) > 0)\n"
     "            await CreatureCmd.Damage(ctx, player.Creature, Relics.FmBonus.ExtraSummonCost(player), ValueProp.Unblockable | ValueProp.Unpowered, null, null);\n"
     "        if (SummonsThisCombat == 0) AtkBonus += Relics.FmBonus.FirstSummonAtk(player);\n"
     "        SummonsThisTurn++; SummonsThisCombat++;"),
    ("    public static int PendingAtkBonus, PendingDefBonus;",
     "    public static int PendingAtkBonus, PendingDefBonus;\n    public static readonly List<int> PendingEquips = new();"),
    ("    public static void OnMonsterDestroyed()\n    {\n        if (MonsterId == 0) return;",
     "    public static async Task OnMonsterDestroyed(PlayerChoiceContext ctx, Player player)\n    {\n        if (MonsterId == 0) return;\n"
     "        int burn = Atk * Relics.FmBonus.DeathBurnPercent(player) / 100;\n"
     "        var foes = player.Creature.CombatState?.HittableEnemies.Where(e => e.IsAlive).ToList();\n"
     "        if (burn > 0 && foes is { Count: > 0 })\n"
     "            await CreatureCmd.Damage(ctx, foes[Random.Shared.Next(foes.Count)], burn, ValueProp.Unpowered, player.Creature, SourceCard);\n"
     "        Equips.Clear();"),
])

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    ("    public override Task AfterDeath(PlayerChoiceContext ctx, Creature creature, bool wasRemovalPrevented, float deathAnimLength)\n    {\n"
     "        if (creature.Monster is Osty && creature.PetOwner == Owner) FmField.OnMonsterDestroyed();\n        return Task.CompletedTask;\n    }",
     "    public override async Task AfterDeath(PlayerChoiceContext ctx, Creature creature, bool wasRemovalPrevented, float deathAnimLength)\n    {\n"
     "        if (creature.Monster is Osty && creature.PetOwner == Owner) await FmField.OnMonsterDestroyed(ctx, Owner);\n    }"),
    ("return Math.Max(0, FmRules.HandSize - player.PlayerCombatState.Hand.Cards.Count);",
     "return Math.Max(0, FmBonus.HandSize(player) - player.PlayerCombatState.Hand.Cards.Count);"),
    ("FmField.SummonedThisTurn = false;\n            FmField.AttackedThisTurn = false;",
     "FmField.SummonedThisTurn = false;\n            FmField.SummonsThisTurn = 0;\n            FmField.AttackedThisTurn = false;"),
    ("await CreatureCmd.Damage(ctx, attacker, d.atk, ValueProp.Unpowered, Owner.Creature, FmField.SourceCard);",
     "await CreatureCmd.Damage(ctx, attacker, d.atk * FmBonus.CounterMultiplier(Owner), ValueProp.Unpowered, Owner.Creature, FmField.SourceCard);"),
])

edit('src/FMDuelist/Cards/FmMonsterCard.cs', [
    ("protected override bool IsPlayable => !FmField.SummonedThisTurn;", "protected override bool IsPlayable => FmField.CanSummon(Owner);"),
    ("                    steps.Add(new FmFusionStep(cur, fm.FmId, res));\n                    cur = res; atkBonus = defBonus = 0;",
     "                    steps.Add(new FmFusionStep(cur, fm.FmId, res));\n                    fusions++;\n"
     "                    cur = res; atkBonus = Relics.FmBonus.FusionBonus(owner) * fusions; defBonus = atkBonus;"),
    ("                else { steps.Add(new FmFusionStep(cur, fm.FmId, null)); cur = fm.FmId; atkBonus = defBonus = 0; }",
     "                else\n                {\n"
     "                    bool keep = Relics.FmBonus.KeepStronger(owner) && FmData.Get(cur).Atk >= fm.Info.Atk;\n"
     "                    steps.Add(new FmFusionStep(keep ? fm.FmId : cur, keep ? cur : fm.FmId, null));\n"
     "                    if (!keep) { cur = fm.FmId; atkBonus = defBonus = 0; fusions = 0; }\n                }"),
    ("int cur = 0, first = 0, atkBonus = 0, defBonus = 0;",
     "int cur = 0, first = 0, atkBonus = 0, defBonus = 0, fusions = 0;\n        FmField.PendingEquips.Clear();"),
    ("if (ok) { atkBonus += sp.Value; defBonus += sp.Value; }",
     "if (ok) { atkBonus += sp.Value; defBonus += sp.Value; FmField.PendingEquips.Add(fm.FmId); }"),
])

edit('src/FMDuelist/Cards/FmRitualCard.cs', [
    ("protected override bool IsPlayable => !FmField.SummonedThisTurn", "protected override bool IsPlayable => FmField.CanSummon(Owner)")])

edit('src/FMDuelist/Cards/FmSpellCard.cs', [
    ("                await CreatureCmd.Heal(me, Value);", "                await CreatureCmd.Heal(me, Value * Relics.FmBonus.HealMultiplier(Owner));"),
    ("                FmField.AtkBonus += Value; FmField.DefBonus += Value;", "                FmField.AtkBonus += Value; FmField.DefBonus += Value;\n                FmField.Equips.Add(FmId);"),
    ("                await PowerCmd.Apply<FmTrapPower>(ctx, me, 1, me, this);", "                await PowerCmd.Apply<FmTrapPower>(ctx, me, 1, me, this);\n                FmField.NotifyChanged();"),
    ("                FmField.SwordsTurns = Value;", "                FmField.SwordsTurns = Value;\n                FmField.NotifyChanged();"),
])

edit('src/FMDuelist/FmMod.cs', [
    ("        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, DragonTreasure>();\n",
     "        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, DragonTreasure>();\n"
     "        foreach (var t in FmRelicSheet.All) ModHelper.AddModelToPool(typeof(MegaCrit.Sts2.Core.Models.RelicPools.NecrobinderRelicPool), t);\n"),
    ("static int? ArtFor(RelicModel r) => r switch { MillenniumPuzzle => 362, DragonTreasure => 315, _ => null };",
     "static int? ArtFor(RelicModel r) => r switch { MillenniumPuzzle => 362, DragonTreasure => 315, FmRelic f => f.ArtId, _ => null };"),
])

edit('src/FMDuelist/FmLoc.cs', [(
    '            loc.GetTable("powers").MergeWith(',
    '            var relicText = new Dictionary<string, string>();\n'
    '            foreach (var (t, (title, text)) in FmRelicSheet.Text)\n            {\n'
    '                relicText[E(t) + ".title"] = title;\n                relicText[E(t) + ".description"] = text;\n'
    '                relicText[E(t) + ".flavor"] = "A memory from Forbidden Memories.";\n            }\n'
    '            loc.GetTable("relics").MergeWith(relicText);\n\n'
    '            loc.GetTable("powers").MergeWith(')])
print('patched')
