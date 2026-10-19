using FMDuelist.Data;
using FMDuelist.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Cards;

public abstract class FmMonsterCard : FmCardBase
{
    protected FmMonsterCard(int fmId) : base(fmId, CardType.Attack, TargetType.AnyEnemy) { }

    protected override bool IsPlayable => FmField.CanSummon(Owner);

    protected override bool ShouldGlowGoldInternal => FmField.Marked.Contains(this);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay cardPlay)
    {
        FmFieldTarget.MonsterArrow = false;
        // arrow dropped on one of your own field monsters: fuse onto it (it leads the chain), no zone prompt
        var onField = FmFieldTarget.SlotOf(this, cardPlay.Target);
        // the zone choice can be cancelled: nothing has been used yet, the card goes back to your hand (marks kept)
        var zone = onField ?? await FmFusion.ChooseZone(ctx, Owner, this, canCancel: true);
        if (zone == null) { await CancelSummon(); return; }
        var chain = FmFusion.CollectChain(this, Owner);
        int fieldMonster = zone.Alive(Owner) ? zone.MonsterId : 0;
        // a plain summon (no other cards, no field monster) can still be cancelled at the position choice
        bool canCancel = chain.Count == 1 && fieldMonster == 0;
        int resultId = await FmFusion.Resolve(ctx, chain, Owner, this, fieldMonster);
        if (resultId == 0)
        {
            if (zone.Alive(Owner) && zone.Creature(Owner) is { } gone) await MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(gone);
            FmField.SummonsThisTurn++;
            FmField.NotifyChanged();
            return;
        }
        var pos = await FmFusion.ChoosePosition(ctx, Owner, resultId, this, canCancel);
        if (pos == null) { await CancelSummon(); return; }
        await FmField.Summon(ctx, Owner, zone, resultId, pos.Value, onField != null ? null : cardPlay.Target, this);
    }

    /// <summary>Back to the hand: the summon isn't used and nothing else changes.</summary>
    async Task CancelSummon()
    {
        await CardPileCmd.Add(this, PileType.Hand);
        FmField.NotifyChanged();
        Log.Info($"[FMDuelist] Summon of {Info.Name} cancelled");
    }
}

/// <summary>The FM fusion chain: marked cards resolve left to right using the disc's fusion table.</summary>
public static class FmFusion
{
    public static List<CardModel> CollectChain(CardModel played, Player owner)
    {
        var hand = owner.PlayerCombatState!.Hand.Cards;
        var chain = FmField.Marked.Where(c => c == played || hand.Contains(c)).ToList();
        if (!chain.Contains(played)) chain.Add(played); // an unmarked card played during a chain joins it last
        // junk marked before the first monster is used right after it (junk never starts a chain)
        int firstMonster = chain.FindIndex(c => c is FmMonsterCard);
        if (firstMonster > 0)
        {
            var lead = chain.Take(firstMonster).Where(c => FmJunk.Kind(c) != JunkKind.None).ToList();
            chain.RemoveAll(lead.Contains);
            chain.InsertRange(chain.FindIndex(c => c is FmMonsterCard) + 1, lead);
        }
        FmField.Marked.Clear();
        FmField.NotifyChanged();
        return chain;
    }

    public static bool DragonAllowed(int id, Player owner)
    {
        var c = FmData.Get(id);
        return c.Type != FmType.Dragon || c.Level < FmRules.StrongDragonLevel || owner.GetRelic<Relics.DragonTreasure>() != null;
    }

    /// <summary>
    /// Pick the zone to summon into. An empty zone places the monster; an occupied zone puts its monster first
    /// in the fusion chain (FM: fusing onto the field). Skipped when every zone is empty.
    /// </summary>
    public static async Task<FmSlot?> ChooseZone(PlayerChoiceContext ctx, Player owner, CardModel source, bool canCancel = false)
    {
        FmField.Duelist = owner;
        FmDuelLayout.PlaceAll(owner);
        // FM: pick the zone on the field itself (all five, empty or not)
        int id = source is FmCardBase fm ? fm.FmId : 0;
        return await Game.FmZonePicker.Pick(owner, id, canCancel);
    }

    /// <summary>Returns the monster that ends up summoned. Material cards other than the played one go to the discard pile.</summary>
    public static async Task<int> Resolve(PlayerChoiceContext ctx, List<CardModel> chain, Player owner, CardModel played, int fieldMonster)
    {
        if (FmRival.Active && chain.Count > 1) { FmDuelStats.Fusions++; FmDuelStats.You.CardsUsed += chain.Count - 1; }
        int cur = fieldMonster, first = fieldMonster, atkBonus = 0, defBonus = 0, fusions = 0;
        FmField.PendingEquips.Clear();
        var steps = new List<FmFusionStep>();
        var destroyed = new List<CardModel>();
        // spells/traps/equips marked before any monster: they meet the first monster when it arrives (FM-style)
        var waiting = new List<CardModel>();
        void Meet(CardModel card)
        {
            if (card is FmSpellCard { Kind: FmSpellKind.Equip } eq)
            {
                bool ok = FmData.CanEquip(eq.FmId, cur);
                if (ok) { steps.Add(new FmFusionStep(cur, eq.FmId, null, true, true)); FmField.PendingEquips.Add((eq.FmId, eq.Value)); }
                else steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.JunkFail)); // wrong monster: knocked away
            }
            else steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.JunkFail));
        }
        foreach (var card in chain)
        {
            var junk = FmJunk.Kind(card);
            if (junk != JunkKind.None)
            {
                if (cur == 0) { if (junk != JunkKind.Harmful) waiting.Add(card); continue; }
                if (junk == JunkKind.Harmful)
                {
                    // it takes the monster down with it: both are destroyed
                    Log.Info($"[FMDuelist] Fusion {FmData.Get(cur).Name} + {card.Id.Entry}: sacrificed");
                    steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.Sacrifice));
                    destroyed.Add(card);
                    cur = 0; atkBonus = defBonus = 0; fusions = 0;
                }
                else
                {
                    Log.Info($"[FMDuelist] Fusion {FmData.Get(cur).Name} + {card.Id.Entry}: no fusion");
                    steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.JunkFail));
                }
                continue;
            }
            if (card is not FmCardBase fm) continue;
            if (fm is FmMonsterCard)
            {
                if (cur == 0)
                {
                    if (first != 0 || steps.Count > 0) steps.Add(new FmFusionStep(0, fm.FmId, null, Kind: FmStepKind.Start));
                    cur = fm.FmId;
                    if (first == 0) first = cur;
                    foreach (var w in waiting) Meet(w); // the waiting cards are knocked away (or equipped); the monster stays
                    waiting.Clear();
                    continue;
                }
                int? r = FmData.Fuse(cur, fm.FmId);
                if (r is int res && FmData.Get(res).IsMonster && DragonAllowed(res, owner))
                {
                    Log.Info($"[FMDuelist] Fusion {FmData.Get(cur).Name} + {fm.Info.Name} = {FmData.Get(res).Name}");
                    steps.Add(new FmFusionStep(cur, fm.FmId, res));
                    fusions++;
                    cur = res; atkBonus = Relics.FmBonus.FusionBonus(owner) * fusions; defBonus = atkBonus;
                }
                else
                {
                    bool keep = Relics.FmBonus.KeepStronger(owner) && FmData.Get(cur).Atk >= fm.Info.Atk;
                    steps.Add(new FmFusionStep(keep ? fm.FmId : cur, keep ? cur : fm.FmId, null));
                    if (!keep) { cur = fm.FmId; atkBonus = defBonus = 0; fusions = 0; }
                }
            }
            else if (fm is FmSpellCard sp && sp.Kind == FmSpellKind.Equip && cur != 0)
            {
                bool ok = FmData.CanEquip(fm.FmId, cur);
                if (ok) { steps.Add(new FmFusionStep(cur, fm.FmId, null, true, true)); FmField.PendingEquips.Add((fm.FmId, sp.Value)); }
                else steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.JunkFail)); // wrong monster: knocked away
            }
            else if (cur == 0) waiting.Add(card);
            else
            {
                // spells, traps and rituals don't fuse with monsters: knocked away (and out of your hand)
                Log.Info($"[FMDuelist] Fusion {FmData.Get(cur).Name} + {fm.Info.Name}: no fusion");
                steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.JunkFail));
            }
        }
        if (steps.Count > 0)
        {
            try { await FmFusionAnim.Play(steps, first); }
            catch (Exception ex) { Log.Warn("[FMDuelist] fusion anim: " + ex.Message); }
        }
        foreach (var card in chain)
            if (card != played) await CardPileCmd.Add(card, destroyed.Contains(card) ? PileType.Exhaust : PileType.Discard);
        FmField.PendingAtkBonus = atkBonus; FmField.PendingDefBonus = defBonus;
        if (cur == 0 && destroyed.Count == 0 && played is FmCardBase p) return p.FmId;
        return cur;
    }

    public static async Task<FmPosition?> ChoosePosition(PlayerChoiceContext ctx, Player owner, int monsterId, CardModel source, bool canCancel = false)
    {
        var scope = source.CardScope!;
        var atk = (FmPositionCard)scope.CreateCard(ModelDb.Card<FmAttackPosition>(), owner);
        var def = (FmPositionCard)scope.CreateCard(ModelDb.Card<FmDefensePosition>(), owner);
        atk.ShowMonster(monsterId); def.ShowMonster(monsterId);
        var pick = await CardSelectCmd.FromChooseACardScreen(ctx, new CardModel[] { atk, def }, owner, canSkip: canCancel);
        if (pick == null && canCancel) return null;
        return pick == def ? FmPosition.Defense : FmPosition.Attack;
    }

    /// <summary>Pick one of the given monsters on the field (shows their cards).</summary>
    public static async Task<FmSlot?> ChooseMonster(PlayerChoiceContext ctx, Player owner, AbstractModel source, List<FmSlot> slots)
    {
        var scope = owner.Creature.CombatState as MegaCrit.Sts2.Core.Runs.ICardScope ?? owner.RunState;
        var options = slots.Select(s => scope.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(s.MonsterId))), owner)).ToList();
        var pick = await CardSelectCmd.FromChooseACardScreen(ctx, options, owner, canSkip: false);
        int i = pick == null ? 0 : Math.Max(0, options.IndexOf(pick));
        return slots[i];
    }

    /// <summary>Which living monster gets an equip (asks only when more than one can take it).</summary>
    public static async Task<FmSlot?> ChooseEquipTarget(PlayerChoiceContext ctx, Player owner, CardModel source, int equipId)
    {
        var ok = FmField.Living(owner).Where(s => FmData.CanEquip(equipId, s.MonsterId)).ToList();
        if (ok.Count <= 1) return ok.FirstOrDefault();
        var scope = source.CardScope!;
        var options = ok.Select(s => scope.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(s.MonsterId))), owner)).ToList();
        var pick = await CardSelectCmd.FromChooseACardScreen(ctx, options, owner, canSkip: false);
        int i = pick == null ? 0 : Math.Max(0, options.IndexOf(pick));
        return ok[i];
    }
}
