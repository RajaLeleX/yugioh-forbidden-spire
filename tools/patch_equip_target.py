import os, json
H = os.path.expanduser('~/fmsts/src/FMDuelist')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# ---- per-monster equips ----
edit('Game/FmField.cs', [
    ("""    public List<int> Equips => MonsterId == 0 ? new List<int>() : FmField.CombatEquips.Where(e => FmData.CanEquip(e.id, MonsterId)).Select(e => e.id).ToList();
    int EquipBonus => MonsterId == 0 ? 0 : FmField.CombatEquips.Where(e => FmData.CanEquip(e.id, MonsterId)).Sum(e => e.value);""",
     """    /// <summary>Equip cards on this monster (id, bonus); they stay while it's on the field.</summary>
    public readonly List<(int id, int value)> EquipList = new();
    public List<int> Equips => EquipList.Select(e => e.id).ToList();
    int EquipBonus => MonsterId == 0 ? 0 : EquipList.Sum(e => e.value);"""),
    ("AttackedThisTurn = false; TargetCombatId = null;", "AttackedThisTurn = false; EquipList.Clear(); TargetCombatId = null;"),
    ("""    public static readonly List<int> PendingEquips = new();
    /// <summary>Every equip played this combat (id, bonus): any monster that can use it gets the bonus for the rest of the combat.</summary>
    public static readonly List<(int id, int value)> CombatEquips = new();""",
     """    /// <summary>Equips used in a fusion chain (id, bonus); they go onto the monster the chain summons.</summary>
    public static readonly List<(int id, int value)> PendingEquips = new();"""),
    ("PendingEquips.Clear(); CombatEquips.Clear(); PendingAtkBonus", "PendingEquips.Clear(); PendingAtkBonus"),
    ("""        PendingAtkBonus = PendingDefBonus = 0; PendingEquips.Clear();""",
     """        slot.EquipList.AddRange(PendingEquips);
        PendingAtkBonus = PendingDefBonus = 0; PendingEquips.Clear();"""),
])
edit('Cards/FmMonsterCard.cs', [
    ("if (ok) { FmField.PendingEquips.Add(fm.FmId); FmField.CombatEquips.Add((fm.FmId, sp.Value)); }",
     "if (ok) FmField.PendingEquips.Add((fm.FmId, sp.Value));"),
])

# ---- equip spells target a monster on the field ----
edit('Cards/FmSpellCard.cs', [
    ("""              : kind is FmSpellKind.Field ? CardType.Power : CardType.Skill, target)""",
     """              : kind is FmSpellKind.Field ? CardType.Power : CardType.Skill, kind == FmSpellKind.Equip ? TargetType.AnyEnemy : target)"""),
    ("""        FmSpellKind.Trap => FmField.CanSetTrap,""",
     """        FmSpellKind.Trap => FmField.CanSetTrap,
        FmSpellKind.Equip => Owner != null && FmField.Living(Owner).Any(s => FmData.CanEquip(FmId, s.MonsterId)),"""),
    ("""                // lasts the whole combat: every monster that can use it gets the bonus, now and later
                FmField.CombatEquips.Add((FmId, Value));
                FmSfx.Play(FmSfx.PowerUp);
                FmField.NotifyChanged();""",
     """                // goes onto the monster the arrow was dropped on
                var eq = FmFieldTarget.SlotOf(this, cardPlay.Target) is { } chosen && FmData.CanEquip(FmId, chosen.MonsterId) ? chosen
                    : FmField.Living(Owner).FirstOrDefault(s => FmData.CanEquip(FmId, s.MonsterId));
                if (eq != null)
                {
                    try { await eq.Visual.EquipFx(FmId, Value); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] equip fx: " + ex.Message); }
                    eq.EquipList.Add((FmId, Value));
                }
                FmField.NotifyChanged();"""),
])
edit('Game/FmFieldTarget.cs', [
    ("""    public static bool MonsterArrow;""",
     """    public static bool MonsterArrow;
    /// <summary>The equip card whose arrow is being dragged (it can only land on monsters that can use it).</summary>
    public static Cards.FmSpellCard? EquipArrow;"""),
    ("""        try { MonsterArrow = Traverse.Create(cardPlay).Property("Card").GetValue() is FmMonsterCard; }
        catch { MonsterArrow = false; }""",
     """        try
        {
            var card = Traverse.Create(cardPlay).Property("Card").GetValue();
            MonsterArrow = card is FmMonsterCard;
            EquipArrow = card is Cards.FmSpellCard { Kind: Cards.FmSpellKind.Equip } sp ? sp : null;
        }
        catch { MonsterArrow = false; EquipArrow = null; }"""),
    ("""        if (!__result && FmFieldTarget.MonsterArrow && creature.IsAlive && FmFieldTarget.IsFieldMonster(creature)) __result = true;""",
     """        if (FmFieldTarget.EquipArrow is { } eq) { __result = creature.IsAlive && FmFieldTarget.CanTakeEquip(eq, creature); return; }
        if (!__result && FmFieldTarget.MonsterArrow && creature.IsAlive && FmFieldTarget.IsFieldMonster(creature)) __result = true;"""),
    ("""    public static bool IsFieldMonster(Creature c) =>""",
     """    public static bool CanTakeEquip(Cards.FmSpellCard equip, Creature c) =>
        equip.Owner is { } p && FmField.Slots.Any(s => s.Alive(p) && s.Creature(p) == c && Data.FmData.CanEquip(equip.FmId, s.MonsterId));

    public static bool IsFieldMonster(Creature c) =>"""),
])
# IsValidTarget: equips only on a monster that can use them
p = f'{H}/Game/FmFieldTarget.cs'
s = open(p, encoding='utf-8').read()
i = s.index('static class FieldMonsterValidTargetPatch')
j = s.index('    static void Postfix', i)
k = s.index('{', j)
s = s[:k + 1] + """
        if (__instance is Cards.FmSpellCard { Kind: Cards.FmSpellKind.Equip } eq) { __result = target != null && target.IsAlive && FmFieldTarget.CanTakeEquip(eq, target); return; }""" + s[k + 1:]
open(p, 'w', encoding='utf-8').write(s)

# reset the equip arrow whenever the field-attack arrow starts
edit('Game/FmUi.cs', [("            FmFieldTarget.MonsterArrow = false;\n", "            FmFieldTarget.MonsterArrow = false; FmFieldTarget.EquipArrow = null;\n")])
print('ok')
