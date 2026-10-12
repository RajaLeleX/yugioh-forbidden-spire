using FMDuelist.Game;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace FMDuelist.Cards;

/// <summary>The two cards of the Attack/Defense choice window. They show the monster being summoned.</summary>
public abstract class FmPositionCard : FmCardBase
{
    public FmPosition Position { get; }
    protected FmPositionCard(FmPosition p) : base(0, CardType.Skill, TargetType.None) { Position = p; }
    public void ShowMonster(int id) => FmId = id;
    public override CardRarity Rarity => CardRarity.Token;
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay cardPlay) => Task.CompletedTask;
}

public sealed class FmAttackPosition : FmPositionCard { public FmAttackPosition() : base(FmPosition.Attack) { } }
public sealed class FmDefensePosition : FmPositionCard { public FmDefensePosition() : base(FmPosition.Defense) { } }

/// <summary>An empty monster zone in the zone-choice window.</summary>
public sealed class FmEmptyZone : FmPositionCard
{
    public int Zone;
    public FmEmptyZone() : base(FmPosition.Attack) { }
}
