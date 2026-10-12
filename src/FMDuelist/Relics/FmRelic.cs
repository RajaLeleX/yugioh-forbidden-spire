using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Game;
using FMDuelist.Powers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace FMDuelist.Relics;

public enum FmRelicKind { HandSize, DeathBurn, CombatWeak, CombatTrap, FusionBonus, EmptyFieldBlock, HealAfterCombat, HealBonus, FirstSummonAtk, CombatDraw, TypeBoost, CounterBonus, DefenseBoost, StartField, DoubleSummon, FusionKeep }

/// <summary>A relic row from sheets/relics.json. Passive kinds are read through FmBonus; active kinds use hooks here.</summary>
public abstract class FmRelic : RelicModel
{
    public FmRelicKind Kind { get; }
    public int Value { get; }
    public int ArtId { get; }
    public FmType[] Types { get; }
    private readonly RelicRarity _rarity;

    protected FmRelic(FmRelicKind kind, int value, int art, RelicRarity rarity, FmType[]? types)
    {
        Kind = kind; Value = value; ArtId = art; _rarity = rarity; Types = types ?? Array.Empty<FmType>();
    }

    public override RelicRarity Rarity => _rarity;

    public override bool IsAllowed(MegaCrit.Sts2.Core.Runs.IRunState runState) => runState.Players.Any(FmRules.IsFmPlayer);

    public override async Task BeforeCombatStart()
    {
        var me = Owner.Creature;
        var ctx = new ThrowingPlayerChoiceContext();
        switch (Kind)
        {
            case FmRelicKind.CombatWeak:
                Flash();
                await PowerCmd.Apply<WeakPower>(ctx, me.CombatState!.Enemies.Where(e => e.IsAlive), Value, me, null);
                break;
            case FmRelicKind.CombatTrap:
                Flash();
                FmField.Traps.Add(Value);
                await PowerCmd.Apply<FmTrapPower>(ctx, me, 1, me, null);
                FmField.NotifyChanged();
                break;
            case FmRelicKind.StartField:
                if (ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(Value))) is FmSpellCard field)
                {
                    Flash();
                    FmField.FieldBoost.Clear();
                    foreach (var t in field.BoostTypes) FmField.FieldBoost.Add(t);
                    await PowerCmd.Apply<FmFieldPower>(ctx, me, Value, me, null);
                    FmBackdrop.Set(Value);
                    FmField.NotifyChanged();
                }
                break;
        }
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player != Owner) return;
        if (Kind == FmRelicKind.CombatDraw && player.PlayerCombatState?.TurnNumber == 1)
        {
            Flash();
            await CardPileCmd.Draw(ctx, Value, player);
        }
        if (Kind == FmRelicKind.EmptyFieldBlock && !FmField.HasMonster(player))
        {
            Flash();
            await FmRules.Raw(() => CreatureCmd.GainBlock(player.Creature, Value, ValueProp.Unpowered, null));
        }
    }

    public override async Task AfterCombatVictory(CombatRoom room)
    {
        if (Kind != FmRelicKind.HealAfterCombat || Owner.Creature.IsDead) return;
        Flash();
        await FmRules.Raw(() => CreatureCmd.Heal(Owner.Creature, Value));
    }
}

/// <summary>Passive relic effects, read by the duel rules.</summary>
public static class FmBonus
{
    static IEnumerable<FmRelic> All(Player? p) => p?.Relics.OfType<FmRelic>() ?? Enumerable.Empty<FmRelic>();
    static int Sum(Player? p, FmRelicKind k) => All(p).Where(r => r.Kind == k).Sum(r => r.Value);
    public static bool Has(Player? p, FmRelicKind k) => All(p).Any(r => r.Kind == k);

    public static int HandSize(Player? p) => FmRules.HandSize + Sum(p, FmRelicKind.HandSize);
    public static int SummonsPerTurn(Player? p) => Has(p, FmRelicKind.DoubleSummon) ? 2 : 1;
    public static int ExtraSummonCost(Player? p) => Sum(p, FmRelicKind.DoubleSummon);
    public static int FusionBonus(Player? p) => Sum(p, FmRelicKind.FusionBonus);
    public static int FirstSummonAtk(Player? p) => Sum(p, FmRelicKind.FirstSummonAtk);
    public static decimal HealMultiplier(Player? p) => 1m + Sum(p, FmRelicKind.HealBonus) / 100m;
    public static int CounterMultiplier(Player? p) => Has(p, FmRelicKind.CounterBonus) ? 2 : 1;
    public static int DeathBurnPercent(Player? p) => Sum(p, FmRelicKind.DeathBurn);
    public static bool KeepStronger(Player? p) => Has(p, FmRelicKind.FusionKeep);

    public static int TypeBonus(Player? p, int monsterId)
    {
        var t = FmData.Get(monsterId).Type;
        return All(p).Where(r => r.Kind == FmRelicKind.TypeBoost && r.Types.Contains(t)).Sum(r => r.Value);
    }

    public static int DefenseBonus(Player? p) => Sum(p, FmRelicKind.DefenseBoost);
}
