using FMDuelist.Game;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;

namespace FMDuelist.Cards;

/// <summary>Offer any 3 monsters from hand to summon the ritual monster (counts as the turn's summon).</summary>
public abstract class FmRitualCard : FmCardBase
{
    public int ResultId { get; }
    public int Materials { get; }

    protected FmRitualCard(int fmId, int resultId, int materials) : base(fmId, CardType.Skill, TargetType.AnyEnemy)
    {
        ResultId = resultId; Materials = materials;
    }

    public override int ArtId => FmId;

    protected override bool IsPlayable => FmField.CanSummon(Owner)
        && Owner?.PlayerCombatState?.Hand.Cards.Count(c => c is FmMonsterCard) >= Materials
        && FmFusion.DragonAllowed(ResultId, Owner);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay cardPlay)
    {
        var prefs = new CardSelectorPrefs(new LocString("card_selection", "FMDUELIST_RITUAL"), Materials, Materials);
        var offered = await CardSelectCmd.FromHand(ctx, Owner, prefs, c => c is FmMonsterCard, this);
        foreach (var c in offered.ToList()) await CardPileCmd.Add(c, PileType.Discard);
        FmSfx.Play(FmSfx.Fusion);
        // the offerings are already spent, so a ritual summon can't be cancelled
        var zone = await FmFusion.ChooseZone(ctx, Owner, this) ?? FmField.Slots[0];
        var pos = await FmFusion.ChoosePosition(ctx, Owner, ResultId, this) ?? FmPosition.Attack;
        await FmField.Summon(ctx, Owner, zone, ResultId, pos, cardPlay.Target, this);
    }
}
