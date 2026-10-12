using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Cards;

public static partial class FmSheet
{
    /// <summary>Rarity in this act; a card not offered this act shows its first later rarity (it is filtered from rewards).</summary>
    public static CardRarity RarityFor(int fmId, int actIndex)
    {
        if (!_rarity.TryGetValue(fmId, out var r)) return CardRarity.Token;
        for (int a = Math.Clamp(actIndex, 0, 2); a < 3; a++)
            if (r[a] != CardRarity.None) return r[a];
        return CardRarity.Rare;
    }

    public static bool OfferedInAct(CardModel c, int actIndex) =>
        c is not FmCardBase fm || (_rarity.TryGetValue(fm.FmId, out var r) && r[Math.Clamp(actIndex, 0, 2)] != CardRarity.None);
}
