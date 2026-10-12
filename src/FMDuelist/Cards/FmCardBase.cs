using FMDuelist.Data;
using FMDuelist.Game;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Cards;

/// <summary>Every card generated from a sheet row. Its name, art and stats come from the player's disc at runtime.</summary>
public abstract class FmCardBase : CardModel
{
    public int FmId { get; protected set; }

    protected FmCardBase(int fmId, CardType type, TargetType target)
        : base(0, type, CardRarity.Common, target)
    {
        FmId = fmId;
    }

    public FmCardInfo Info => FmData.Get(FmId);

    /// <summary>Rarity for the current act (sheet columns act1/act2/act3). None = not offered this act.</summary>
    public override CardRarity Rarity => FmSheet.RarityFor(FmId, FmRules.ActIndex);

    public override CardPoolModel Pool => FmSheet.ColorlessIds.Contains(FmId) && this is not FmPositionCard
        ? ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>() : ModelDb.CardPool<FmCardPool>();

    public override int MaxUpgradeLevel => 0;

    public virtual int ArtId => FmId;
}
