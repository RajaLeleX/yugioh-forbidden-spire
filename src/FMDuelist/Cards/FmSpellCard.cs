using FMDuelist.Data;
using FMDuelist.Game;
using FMDuelist.Powers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace FMDuelist.Cards;

public enum FmSpellKind { Burn, BurnSelfDmg, BurnAll, DarkHole, Heal, Invuln, StopDefense, StripBuffs, Stun, VulnerableAll, Field, Equip, Trap, Block, BlockWeak, BlockCleanse, BlockHeal, BurnStrongest, WeakAll, StripBlockAll }

public abstract class FmSpellCard : FmCardBase
{
    public FmSpellKind Kind { get; }
    public int Value { get; }
    public FmType[] BoostTypes { get; }
    private readonly bool _exhaust;

    protected FmSpellCard(int fmId, FmSpellKind kind, int value, TargetType target, bool exhaust, FmType[]? boost = null)
        : base(fmId, kind is FmSpellKind.Burn or FmSpellKind.BurnSelfDmg or FmSpellKind.BurnAll or FmSpellKind.DarkHole or FmSpellKind.BurnStrongest or FmSpellKind.StripBlockAll ? CardType.Attack
              : kind is FmSpellKind.Field ? CardType.Power : CardType.Skill, kind == FmSpellKind.Equip ? TargetType.AnyEnemy : target)
    {
        Kind = kind; Value = value; _exhaust = exhaust; BoostTypes = boost ?? Array.Empty<FmType>();
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => _exhaust ? new[] { CardKeyword.Exhaust } : Array.Empty<CardKeyword>();

    protected override bool IsPlayable => Kind switch
    {
        FmSpellKind.Trap => FmField.CanSetTrap,
        FmSpellKind.Equip => Owner != null && FmField.Living(Owner).Any(s => FmData.CanEquip(FmId, s.MonsterId)),
        // Dark Hole can't follow an attack: not after any of your monsters attacked this turn
        FmSpellKind.DarkHole => !FmField.MassSpellThisTurn && !FmField.Slots.Any(s => s.AttackedThisTurn),
        _ when IsMassSpell => !FmField.MassSpellThisTurn,
        _ => true,
    };

    /// <summary>Raigeki (337) and Dark Hole (336): only one of them per turn.</summary>
    public bool IsMassSpell => FmId is 336 or 337;

    protected override bool ShouldGlowGoldInternal => FmField.Marked.Contains(this);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay cardPlay)
    {
        var me = Owner.Creature;
        var combat = me.CombatState!;
        var enemies = combat.HittableEnemies.Where(e => e.IsAlive).ToList();
        if (IsMassSpell) { FmField.MassSpellThisTurn = true; FmField.NotifyChanged(); }
        switch (Kind)
        {
            case FmSpellKind.Burn:
                await CreatureCmd.Damage(ctx, cardPlay.Target!, Value, ValueProp.Unpowered, me, this);
                break;
            case FmSpellKind.BurnSelfDmg:
                await CreatureCmd.Damage(ctx, cardPlay.Target!, Value, ValueProp.Unpowered, me, this);
                await CreatureCmd.Damage(ctx, me, Value / 2, ValueProp.Unpowered, me, this);
                break;
            case FmSpellKind.BurnAll:
                if (FmId == 337) { try { await FmSpellVfx.Raigeki(enemies); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] raigeki vfx: " + ex.Message); } }
                if (FmRival.Active && FmId == 337) { await FmRival.DestroyAll(); break; } // boss duel: destroys his monsters, not him
                await CreatureCmd.Damage(ctx, enemies.Where(e => !FmRival.IsZoneCreature(e)), Value, ValueProp.Unpowered, me, this);
                break;
            case FmSpellKind.DarkHole:
                try { await FmSpellVfx.DarkHole(); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] dark hole vfx: " + ex.Message); }
                if (FmRival.Active) await FmRival.DestroyAll(); // boss duel: every monster goes, nobody takes damage
                else await CreatureCmd.Damage(ctx, enemies, Value, ValueProp.Unpowered, me, this);
                foreach (var z in FmField.Living(Owner).ToList()) if (z.Creature(Owner) is { } zc) await CreatureCmd.Kill(zc);
                FmField.SummonsThisTurn = 1000; // no more summons this turn
                FmField.NotifyChanged();
                break;
            case FmSpellKind.Heal:
            {
                // marked heals fuse up the healing ladder first
                int heal = FmHealFusion.IsHeal(FmId) ? await FmHealFusion.Resolve(Owner, this) : FmId;
                int amount = heal == FmId ? Value : FmHealFusion.HealAmount(heal);
                await FmHealVfx.Play(heal, me);
                await FmRules.Raw(() => CreatureCmd.Heal(me, amount * Relics.FmBonus.HealMultiplier(Owner)));
                break;
            }
            case FmSpellKind.Invuln:
                // Swords of Revealing Light: every enemy is stunned for the next 3 turns (a boss duelist still acts, but can't attack)
                FmField.SwordsTurns = Value;
                FmField.NotifyChanged();
                await PowerCmd.Remove<FmSwordsPower>(me);
                await PowerCmd.Apply<FmSwordsPower>(ctx, me, Value, me, this);
                FmVfx.SwordsOfRevealingLight();
                break;
            case FmSpellKind.StopDefense:
                if (cardPlay.Target is { } t && t.Block > 0) await CreatureCmd.LoseBlock(t, t.Block);
                break;
            case FmSpellKind.StripBuffs:
                foreach (var e in enemies)
                    foreach (var p in e.Powers.Where(p => p.Type == PowerType.Buff).ToList())
                        await PowerCmd.Remove(p);
                break;
            case FmSpellKind.Stun when FmId == 349:
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
                break;
            case FmSpellKind.VulnerableAll:
                await PowerCmd.Apply<VulnerablePower>(ctx, enemies, Value, me, this);
                break;
            case FmSpellKind.Field:
                FmField.FieldBoost.Clear();
                foreach (var b in BoostTypes) FmField.FieldBoost.Add(b);
                await PowerCmd.Remove<FmFieldPower>(me);
                await PowerCmd.Apply<FmFieldPower>(ctx, me, FmId, me, this);
                FmBackdrop.Set(FmId);
                FmField.NotifyChanged();
                break;
            case FmSpellKind.Equip:
                // goes onto the monster the arrow was dropped on
                var eq = FmFieldTarget.SlotOf(this, cardPlay.Target) is { } chosen && FmData.CanEquip(FmId, chosen.MonsterId) ? chosen
                    : FmField.Living(Owner).FirstOrDefault(s => FmData.CanEquip(FmId, s.MonsterId));
                if (eq != null)
                {
                    try { await eq.Visual.EquipFx(FmId, Value); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] equip fx: " + ex.Message); }
                    eq.EquipList.Add((FmId, Value));
                }
                FmField.NotifyChanged();
                break;
            case FmSpellKind.BurnStrongest:
                if (enemies.OrderByDescending(e => e.CurrentHp).FirstOrDefault() is { } big) await CreatureCmd.Damage(ctx, big, Value, ValueProp.Unpowered, me, this);
                break;
            case FmSpellKind.WeakAll:
                await PowerCmd.Apply<WeakPower>(ctx, enemies, Value, me, this);
                break;
            case FmSpellKind.StripBlockAll:
                foreach (var e in enemies) if (e.Block > 0) await CreatureCmd.LoseBlock(e, e.Block);
                await CreatureCmd.Damage(ctx, enemies, Value, ValueProp.Unpowered, me, this);
                break;
            case FmSpellKind.Block:
            case FmSpellKind.BlockWeak:
            case FmSpellKind.BlockCleanse:
            case FmSpellKind.BlockHeal:
                await CreatureCmd.GainBlock(me, Value, ValueProp.Unpowered, cardPlay);
                if (Kind == FmSpellKind.BlockWeak && cardPlay.Target is { } wt) await PowerCmd.Apply<WeakPower>(ctx, wt, 1, me, this);
                if (Kind == FmSpellKind.BlockHeal) { await FmHealVfx.Play(FmId, me); await FmRules.Raw(() => CreatureCmd.Heal(me, Value * Relics.FmBonus.HealMultiplier(Owner))); }
                if (Kind == FmSpellKind.BlockCleanse)
                    foreach (var p in me.Powers.Where(p => p.Type == PowerType.Debuff).ToList()) await PowerCmd.Remove(p);
                break;
            case FmSpellKind.Trap:
                FmField.Traps.Add(FmId);
                await PowerCmd.Apply<FmTrapPower>(ctx, me, 1, me, this);
                FmField.NotifyChanged();
                break;
        }
    }
}

/// <summary>Trap behaviour per sheet kind (trap_* rows of spells.json).</summary>
public enum FmTrapKind { DestroyLe, DestroyAny, ReflectBurn, Weak, ReverseBuffs, Absorb }

public abstract class FmTrapCard : FmSpellCard
{
    public FmTrapKind TrapKind { get; }
    protected FmTrapCard(int fmId, FmTrapKind kind, int value) : base(fmId, FmSpellKind.Trap, value, TargetType.Self, false)
    {
        TrapKind = kind;
    }
}
