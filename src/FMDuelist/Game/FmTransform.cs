using FMDuelist.Cards;
using FMDuelist.Data;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace FMDuelist.Game;

/// <summary>
/// Transforming an FM card: a monster becomes a random monster of a higher rarity (odds by rarity tier), and a spell,
/// trap or equip (or ritual) becomes a random spell, trap or equip. Exodia pieces never come out of a transform.
/// </summary>
public static class FmTransform
{
    static int Tier(CardRarity r) => r switch { CardRarity.Common => 0, CardRarity.Uncommon => 1, CardRarity.Rare => 2, _ => -1 };

    /// <summary>Chance of each target tier (Common, Uncommon, Rare) for a monster of the given tier.</summary>
    public static double[] MonsterOdds(int tier) => tier switch
    {
        < 0 => new[] { 0.55, 0.33, 0.12 },  // starter / basic cards
        0 => new[] { 0.0, 0.75, 0.25 },
        _ => new[] { 0.0, 0.0, 1.0 },       // Uncommon and Rare both go to Rare
    };

    static readonly double[] SpellOdds = { 0.6, 0.3, 0.1 };

    static IEnumerable<int> PoolIds() => FmSheet.PoolTypes
        .Select(t => int.TryParse(t.Name.Replace("FmCard", ""), out int id) ? id : 0)
        .Where(id => id > 0 && id is not (>= 17 and <= 21));

    public static CardModel? Pick(CardModel original, Rng rng)
    {
        if (original is not FmCardBase fm || original.Owner is not { } owner || !FmRules.IsFmPlayer(owner)) return null;
        int act = FmRules.ActIndex;
        bool monster = original is FmMonsterCard;
        var candidates = PoolIds().Where(id => id != fm.FmId && FmData.Get(id).IsMonster == monster && Tier(FmSheet.RarityFor(id, act)) >= 0).ToList();
        if (candidates.Count == 0) return null;
        var odds = monster ? MonsterOdds(Tier(original.Rarity)) : SpellOdds;
        // roll a tier, falling back to the nearest tier that has cards
        double roll = rng.NextFloat(), acc = 0; int tier = 2;
        for (int i = 0; i < 3; i++) { acc += odds[i]; if (roll < acc) { tier = i; break; } }
        List<int> pool = new();
        for (int d = 0; d < 3 && pool.Count == 0; d++)
            foreach (int t in new[] { tier + d, tier - d }.Distinct())
                pool.AddRange(candidates.Where(id => Tier(FmSheet.RarityFor(id, act)) == t));
        if (pool.Count == 0) pool = candidates;
        int pick = pool[rng.NextInt(0, pool.Count)];
        Log.Info($"[FMDuelist] Transform {fm.Info.Name} ({original.Rarity}) -> {FmData.Get(pick).Name} ({FmSheet.RarityFor(pick, act)})");
        return original.CardScope!.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(pick))), owner);
    }
}

[HarmonyPatch]
static class TransformPatch
{
    [HarmonyPrefix, HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateRandomCardForTransform), new[] { typeof(CardModel), typeof(bool), typeof(Rng) })]
    static bool Default(CardModel original, Rng rng, ref CardModel __result) => Swap(original, rng, ref __result);

    [HarmonyPrefix, HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateRandomCardForTransform), new[] { typeof(CardModel), typeof(IEnumerable<CardModel>), typeof(bool), typeof(Rng) })]
    static bool WithOptions(CardModel original, Rng rng, ref CardModel __result) => Swap(original, rng, ref __result);

    static bool Swap(CardModel original, Rng rng, ref CardModel result)
    {
        try
        {
            if (FmTransform.Pick(original, rng) is not { } c) return true;
            result = c;
            return false;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] transform: " + ex.Message); return true; }
    }
}
