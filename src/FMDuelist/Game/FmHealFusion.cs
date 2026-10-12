using FMDuelist.Cards;
using FMDuelist.Data;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Game;

/// <summary>
/// Healing spells fuse into stronger ones (a ladder, so extra heals never clog your hand):
///   Mooyan Curry + Mooyan Curry                  = Red Medicine
///   Mooyan Curry / Red Medicine + Red Medicine   = Goblin's Secret Remedy
///   any heal + Goblin's Secret Remedy            = Soul of the Pure
///   any heal + Soul of the Pure                  = Dian Keto the Cure Master
///   any heal + Dian Keto                         = Dian Keto (top of the ladder)
/// i.e. the result is one step above the stronger of the two. Mark heals with right-click and play one of them.
/// </summary>
public static class FmHealFusion
{
    public static readonly int[] Ladder = { 338, 339, 340, 341, 342 }; // Curry, Red Medicine, Goblin's Remedy, Soul of the Pure, Dian Keto

    public static bool IsHeal(int id) => Array.IndexOf(Ladder, id) >= 0;

    public static int Fuse(int a, int b)
    {
        int ta = Array.IndexOf(Ladder, a), tb = Array.IndexOf(Ladder, b);
        return Ladder[Math.Min(Ladder.Length - 1, Math.Max(ta, tb) + 1)];
    }

    static int Value(int id) => ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))) is FmSpellCard s ? s.Value : 0;

    /// <summary>Fuses the marked heals (with the played one), plays the chain, and returns the heal to cast.</summary>
    public static async Task<int> Resolve(Player owner, FmSpellCard played)
    {
        var hand = owner.PlayerCombatState!.Hand.Cards;
        var chain = FmField.Marked.Where(c => c == played || hand.Contains(c)).ToList();
        if (!chain.Contains(played)) chain.Add(played);
        FmField.Marked.Clear();
        FmField.NotifyChanged();
        if (chain.Count <= 1) return played.FmId;

        // start from the first heal; everything that isn't a heal is knocked away
        var heals = chain.OfType<FmSpellCard>().Where(c => IsHeal(c.FmId)).ToList();
        if (heals.Count == 0) return played.FmId;
        int cur = heals[0].FmId;
        var steps = new List<FmFusionStep>();
        foreach (var card in chain.SkipWhile(c => c != heals[0]).Skip(1))
        {
            if (card is FmSpellCard sp && IsHeal(sp.FmId))
            {
                int r = Fuse(cur, sp.FmId);
                Log.Info($"[FMDuelist] Heal fusion {FmData.Get(cur).Name} + {FmData.Get(sp.FmId).Name} = {FmData.Get(r).Name}");
                steps.Add(new FmFusionStep(cur, sp.FmId, r));
                cur = r;
            }
            else steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.JunkFail));
        }
        if (steps.Count > 0)
        {
            try { await FmFusionAnim.Play(steps, heals[0].FmId); } catch (Exception ex) { Log.Warn("[FMDuelist] heal fusion anim: " + ex.Message); }
        }
        foreach (var c in chain) if (c != played) await CardPileCmd.Add(c, PileType.Discard);
        return cur;
    }

    public static int HealAmount(int id) => Value(id);
}
