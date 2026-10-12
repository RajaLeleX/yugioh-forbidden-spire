using Godot;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Cards;

public sealed class FmCardPool : CardPoolModel
{
    public override string Title => "necrobinder";
    public override string EnergyColorName => "necrobinder";
    public override string CardFrameMaterialPath => "card_frame_pink";
    public override Color DeckEntryCardColor => new Color("C9A43A");
    public override Color EnergyOutlineColor => new Color("803367");
    public override bool IsColorless => false;

    protected override CardModel[] GenerateAllCards() =>
        FmSheet.PoolTypes.Select(t => (CardModel)ModelDb.GetById<CardModel>(ModelDb.GetId(t))).ToArray();
}
