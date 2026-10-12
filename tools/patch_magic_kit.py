import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:70]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# --- the cards ---
patch('Cards/FmSpellCard.cs', [
('''            case FmSpellKind.Invuln:
                FmField.SwordsTurns = Value;
                FmField.NotifyChanged();
                await PowerCmd.Apply<FmSwordsPower>(ctx, me, Value, me, this);
                FmVfx.SwordsOfRevealingLight();
                break;''',
'''            case FmSpellKind.Invuln:
                // Swords of Revealing Light: every enemy is stunned for the next 3 turns (a boss duelist still acts, but can't attack)
                FmField.SwordsTurns = Value;
                FmField.NotifyChanged();
                await PowerCmd.Remove<FmSwordsPower>(me);
                await PowerCmd.Apply<FmSwordsPower>(ctx, me, Value, me, this);
                FmVfx.SwordsOfRevealingLight();
                break;'''),
('''            case FmSpellKind.Stun:
                if (cardPlay.Target is { } st) await CreatureCmd.Stun(st);
                break;''',
'''            case FmSpellKind.Stun when FmId == 349:
                // Spellbinding Circle: every enemy deals 1000 less damage for 2 turns and is stunned for 1 turn
                foreach (var e in enemies.Where(e => e.IsAlive && !FmRival.IsZoneCreature(e)))
                {
                    await PowerCmd.Remove<FmSpellboundPower>(e);
                    await PowerCmd.Apply<FmSpellboundPower>(ctx, e, Value, me, this);
                }
                FmField.BindStun = true;
                FmField.NotifyChanged();
                break;
            case FmSpellKind.Stun:
                if (cardPlay.Target is { } st) await CreatureCmd.Stun(st);
                break;'''),
])
patch('Game/FmField.cs', [
('''    public static int SwordsTurns;''', '''    public static int SwordsTurns;
    /// <summary>Spellbinding Circle was cast: every enemy is stunned for its next turn.</summary>
    public static bool BindStun;'''),
('''Aiming = false; SwordsTurns = 0;''', '''Aiming = false; SwordsTurns = 0; BindStun = false;'''),
('''public int Atk => MonsterId == 0 ? 0 : Rival ? FmData.Get(MonsterId).Atk + AtkBonus + EquipBonus + FmField.FieldBonus(MonsterId) :''',
 '''public int Atk => MonsterId == 0 ? 0 : Rival ? FmData.Get(MonsterId).Atk + AtkBonus + EquipBonus + FmField.FieldBonus(MonsterId) - FmRival.BoundAtk :'''),
])
# --- turn flow: stuns at the end of your turn; Swords no longer blocks damage ---
patch('Relics/MillenniumPuzzle.cs', [
('''        if (side != CombatSide.Player) return;
        await FmField.ResolveEndOfTurn(ctx, Owner);
        await HandLimit(ctx);''', '''        if (side != CombatSide.Player) return;
        await FmField.ResolveEndOfTurn(ctx, Owner);
        await HandLimit(ctx);
        await StunForSpells();'''),
('''    /// <summary>
    /// Hand limit:''', '''    /// <summary>
    /// Swords of Revealing Light (3 turns) and Spellbinding Circle (1 turn) stun every enemy for its coming turn.
    /// A boss duelist isn't stunned by Swords (he still plays cards, he just can't attack); Spellbinding makes him skip his turn.
    /// </summary>
    private async Task StunForSpells()
    {
        bool swords = FmField.SwordsTurns > 0, bind = FmField.BindStun;
        FmField.BindStun = false;
        if (!swords && !bind) return;
        foreach (var e in Owner.Creature.CombatState!.Enemies.Where(e => e.IsAlive).ToList())
        {
            if (FmRival.IsZoneCreature(e)) continue;
            if (e == FmRival.Priest) { if (bind) FmRival.Bound = true; continue; }
            try { await CreatureCmd.Stun(e); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] stun: " + ex.Message); }
        }
    }

    /// <summary>
    /// Hand limit:'''),
('''        if (FmField.SwordsTurns > 0) return 0;
''', ''),
('''        if (FmField.SwordsTurns > 0 && IsMineOrOsty(target)) return 0;
''', ''),
('''                if (FmField.SwordsTurns <= 0)
                    await FmRules.Raw''', '''                await FmRules.Raw'''),
('''public sealed class DragonTreasure : RelicModel''', '''/// <summary>Neow starter pack: Swords of Revealing Light and Spellbinding Circle.</summary>
public sealed class MagicProtectionKit : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override bool IsAllowedInShops => false;

    public override async Task AfterObtained()
    {
        foreach (int id in new[] { 348, 349 })
        {
            var card = Owner.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), Owner);
            await CardPileCmd.Add(card, PileType.Deck);
        }
    }
}

public sealed class DragonTreasure : RelicModel'''),
])
patch('Game/FmEnemyDefense.cs', [('''!FmField.HasMonster(player) || FmField.SwordsTurns > 0) return true;''', '''!FmField.HasMonster(player)) return true;''')])
patch('Game/FmRival.cs', [
('''    public static Creature? Priest;''', '''    public static Creature? Priest;
    /// <summary>Spellbinding Circle: he skips his next turn.</summary>
    public static bool Bound;
    /// <summary>Spellbinding Circle: his monsters attack for 1000 less while it lasts.</summary>
    public static int BoundAtk => Priest?.GetPower<Powers.FmSpellboundPower>() != null ? 1000 : 0;'''),
('''        try
        {
            Draw();
            _visible = Math.Min(HandSize, Hand.Count);''', '''        if (Bound)
        {
            Bound = false;
            Banner($"{Name} is bound by Spellbinding Circle and can't move!");
            await Wait(0.8);
            return;
        }
        try
        {
            Draw();
            _visible = Math.Min(HandSize, Hand.Count);'''),
('''Active = false; Priest = null;''', '''Active = false; Bound = false; Priest = null;'''),
])
patch('Powers/FmPowers.cs', [('''public sealed class FmFieldPower''', '''/// <summary>Spellbinding Circle: deals 1000 less damage; fades after its owner's turns run out.</summary>
public sealed class FmSpellboundPower : FmPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 349;

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        => Owner == dealer && props.IsPoweredAttack() ? -1000m / Game.FmRules.EnemyScale : 0m;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Enemy && participants.Contains(Owner)) await PowerCmd.Decrement(this);
    }
}

public sealed class FmFieldPower''')])
patch('FmMod.cs', [
('''        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, DragonTreasure>();''', '''        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, DragonTreasure>();
        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, MagicProtectionKit>();'''),
('''DragonTreasure => 315,''', '''DragonTreasure => 315, MagicProtectionKit => 348,'''),
('''            if (rng.NextFloat() < FmSheet.NeowExodiaChance) extra.Add(exo);''', '''            if (rng.NextFloat() < FmSheet.NeowExodiaChance) extra.Add(exo);
            if (rng.NextFloat() < FmSheet.NeowKitChance)
                extra.Add((EventOption)RelicOption.Invoke(__instance, new object?[] { ModelDb.Relic<MagicProtectionKit>().ToMutable(), "INITIAL", "NEOW.pages.DONE.POSITIVE.description" })!);'''),
('''dragon={extra.Contains(opt)} exodia={extra.Contains(exo)}''', '''dragon={extra.Contains(opt)} exodia={extra.Contains(exo)} kit={extra.Count(o => o != opt && o != exo) > 0}'''),
])
patch('FmLoc.cs', [
('''                [E(typeof(DragonTreasure)) + ".title"] = "Dragon Treasure",''', '''                [E(typeof(MagicProtectionKit)) + ".title"] = "Magic Protection Kit",
                [E(typeof(MagicProtectionKit)) + ".description"] = "Add Swords of Revealing Light and Spellbinding Circle to your deck.",
                [E(typeof(MagicProtectionKit)) + ".flavor"] = "A duelist never leaves home without a defense.",
                [E(typeof(DragonTreasure)) + ".title"] = "Dragon Treasure",'''),
('''                [E(typeof(FmSwordsPower)) + ".description"] = "You and your monster take no damage. Turns left shown.",
                [E(typeof(FmSwordsPower)) + ".smartDescription"] = "You and your monster take no damage. Turns left shown.",''',
'''                [E(typeof(FmSwordsPower)) + ".description"] = "Every enemy is stunned on its turn. A boss duelist can still play cards but can't attack. Turns left shown.",
                [E(typeof(FmSwordsPower)) + ".smartDescription"] = "Every enemy is stunned on its turn. A boss duelist can still play cards but can't attack. Turns left shown.",
                [E(typeof(FmSpellboundPower)) + ".title"] = "Spellbinding Circle",
                [E(typeof(FmSpellboundPower)) + ".description"] = "Its attacks deal 1000 less damage. Turns left shown.",
                [E(typeof(FmSpellboundPower)) + ".smartDescription"] = "Its attacks deal 1000 less damage. Turns left shown.",'''),
('''                FmSpellKind.Invuln => $"For {s.Value} turns, you and your monster take no damage.",''', '''                FmSpellKind.Invuln => $"Stun ALL enemies for {s.Value} turns. In a boss duel the boss can still play cards, but can't attack.",'''),
('''                FmSpellKind.Stun => "Stun an enemy: it can't act next turn.",''', '''                FmSpellKind.Stun when m.FmId == 349 => $"ALL enemies deal 1000 less damage for {s.Value} turns and are stunned for 1 turn (a boss duelist skips his turn).",
                FmSpellKind.Stun => "Stun an enemy: it can't act next turn.",'''),
])
print('ok')
