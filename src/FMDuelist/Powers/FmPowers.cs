using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Powers;

public abstract class FmPower : PowerModel
{
    /// <summary>FM card whose art is used as this power's icon.</summary>
    public abstract int IconCardId { get; }
}

public sealed class FmSwordsPower : FmPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 348;
}

public sealed class FmTrapPower : FmPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 686;
}

/// <summary>Shadow Spell: the enemy has lost this much ATK and DEF (FM points) for the combat.</summary>
public sealed class FmShadowPower : FmPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 669;

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        => Owner == dealer && props.IsPoweredAttack() ? -Amount / (decimal)Game.FmRules.EnemyScale : 0m;

    public override decimal ModifyBlockAdditive(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
        => Owner == target && Game.FmRules.RawDepth == 0 ? -Amount / (decimal)Game.FmRules.EnemyScale : 0m;
}

public sealed class FmAtkLossPower : FmPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 669;

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        => Owner == dealer && props.IsPoweredAttack() ? -Amount / (decimal)Game.FmRules.EnemyScale : 0m;
}

public sealed class FmDefLossPower : FmPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 349;

    public override decimal ModifyBlockAdditive(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
        => Owner == target && Game.FmRules.RawDepth == 0 ? -Amount / (decimal)Game.FmRules.EnemyScale : 0m;
}

/// <summary>Burn: 100 damage per stack at the start of the owner's turn, then one stack fades.</summary>
public sealed class FmBurnPower : FmPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 345; // Final Flame

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (!participants.Contains(Owner)) return;
        int dmg = Amount * 100;
        await Game.FmRules.Raw(() => CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), Owner, dmg, ValueProp.Unblockable | ValueProp.Unpowered, null, null));
        if (Owner.IsAlive) await PowerCmd.Decrement(this);
    }
}

/// <summary>Counter Trap: every enemy attack on you or your monsters costs the attacker 100 per stack.</summary>
public sealed class FmCounterTrapPower : FmPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 683;

    public override async Task AfterDamageReceived(PlayerChoiceContext ctx, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer == null || !dealer.IsEnemy || !dealer.IsAlive || !props.IsPoweredAttack()) return;
        if (target != Owner && target.PetOwner?.Creature != Owner) return;
        int dmg = Amount * 100;
        await Game.FmRules.Raw(() => CreatureCmd.Damage(ctx, dealer, dmg, ValueProp.Unpowered, Owner, null));
    }
}

/// <summary>Spellbinding Circle: deals 1000 less damage; fades after its owner's turns run out.</summary>
public sealed class FmSpellboundPower : FmPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int IconCardId => 349;

    // a monster deals 1000 less; a duelist's attacks come from his monsters, which lose 500 ATK/DEF instead (FmRival.BoundAtk)
    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        => Owner == dealer && props.IsPoweredAttack() && !Game.FmRival.IsDuelist(Owner) ? -1000m / Game.FmRules.EnemyScale : 0m;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Enemy && participants.Contains(Owner)) await PowerCmd.Decrement(this);
    }
}

public sealed class FmFieldPower : FmPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override int IconCardId => Amount is >= 330 and <= 335 ? Amount : 330;
    public override int DisplayAmount => 0;
}
