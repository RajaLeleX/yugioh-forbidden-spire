using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Relics;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' Build Deck. Every card you own is in your Chest (your Slay the Spire deck); you duel with a
/// Deck of up to 40 of them. Cards you've won in the last 3 combats are marked NEW. The Deck can be changed before
/// each duel, and from the Deck option in shops and rest sites. Duelists bring as many cards as you do.
/// </summary>
public static class FmDeckBuild
{
    public const int MaxDeck = 40, MinDeck = 20, NewFor = 3;

    static MillenniumPuzzle? Pz(Player p) => p.GetRelic<MillenniumPuzzle>();

    /// <summary>Your owned cards (the Chest), as FM card numbers.</summary>
    public static List<int> Chest(Player p) => p.Deck.Cards.OfType<FmCardBase>().Where(c => c.FmId > 0).Select(c => c.FmId).ToList();

    /// <summary>
    /// Your Deck (FM card numbers, up to 40), checked against what you still own. With nothing chosen yet, it's the
    /// first 40 cards you own.
    /// </summary>
    public static List<int> Deck(Player p)
    {
        var chest = Chest(p);
        var chosen = Pz(p)?.DeckSel ?? Array.Empty<int>();
        if (chosen.Length == 0) return chest.Take(MaxDeck).ToList();
        var left = chest.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
        var deck = new List<int>();
        foreach (var id in chosen)
            if (left.TryGetValue(id, out int n) && n > 0 && deck.Count < MaxDeck) { deck.Add(id); left[id] = n - 1; }
        // a deck never ends up empty: top it up from the chest if cards it named are gone
        if (deck.Count < Math.Min(MinSize(p), chest.Count))
            foreach (var (id, n) in left.ToList())
                for (int k = 0; k < n && deck.Count < Math.Min(MinSize(p), chest.Count); k++) { deck.Add(id); left[id]--; }
        return deck;
    }

    /// <summary>A deck can't go under 20 cards (or under what you own, if you own fewer).</summary>
    public static int MinSize(Player p) => Math.Min(MinDeck, Chest(p).Count);

    public static void Save(Player p, List<int> deck)
    {
        if (Pz(p) is { } pz) pz.DeckSel = deck.Take(MaxDeck).ToArray();
    }

    /// <summary>How many cards the duel deck holds (duelists bring the same number).</summary>
    public static int DeckSize(Player p) => Math.Max(1, Deck(p).Count);

    // ---------------- NEW cards ----------------
    /// <summary>A card joined your Chest: it's NEW for the next 3 combats.</summary>
    public static void Acquired(Player p, int fmId)
    {
        if (Pz(p) is not { } pz || fmId <= 0) return;
        pz.NewIds = pz.NewIds.Append(fmId).ToArray();
        pz.NewAt = pz.NewAt.Append(pz.Combats).ToArray();
        // a card won while you play a fixed deck goes into the deck if there's room
        var chosen = pz.DeckSel;
        if (chosen.Length > 0 && chosen.Length < MaxDeck) pz.DeckSel = chosen.Append(fmId).ToArray();
    }

    public static void CombatDone(Player p)
    {
        if (Pz(p) is not { } pz) return;
        pz.Combats++;
        // forget marks older than 3 combats
        var keep = Enumerable.Range(0, Math.Min(pz.NewIds.Length, pz.NewAt.Length)).Where(i => pz.Combats - pz.NewAt[i] < NewFor).ToList();
        pz.NewIds = keep.Select(i => pz.NewIds[i]).ToArray();
        pz.NewAt = keep.Select(i => pz.NewAt[i]).ToArray();
    }

    public static bool IsNew(Player p, int fmId) => Pz(p) is { } pz && pz.NewIds.Contains(fmId);

    // ---------------- combat: only the Deck is used ----------------
    /// <summary>At the start of a fight: the cards outside your Deck sit this one out.</summary>
    public static async Task ApplyToCombat(Player p)
    {
        try
        {
            var want = Deck(p).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
            var drop = new List<CardModel>();
            foreach (var c in PileType.Draw.GetPile(p).Cards.ToList())
            {
                if (c is not FmCardBase fm || fm.FmId <= 0) continue;
                if (want.TryGetValue(fm.FmId, out int n) && n > 0) want[fm.FmId] = n - 1;
                else drop.Add(c);
            }
            if (drop.Count > 0) await CardPileCmd.RemoveFromCombat(drop, skipVisuals: true);
            Log.Info($"[FMDuelist] duel deck: {PileType.Draw.GetPile(p).Cards.Count} cards ({drop.Count} left in the chest)");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] duel deck: " + ex.Message); }
    }

    /// <summary>You couldn't draw up to a full hand: the duel (and, outside Free Duels, the run) is lost.</summary>
    public static bool DeckedOut;

    public static async Task CheckDeckOut(Player p, int handSize)
    {
        if (p.PlayerCombatState == null || p.Creature.IsDead) return;
        if (PileType.Hand.GetPile(p).Cards.Count >= handSize || PileType.Draw.GetPile(p).Cards.Count > 0) return;
        DeckedOut = true;
        Log.Info("[FMDuelist] deck out: no cards left to draw");
        if (FmRival.Active) FmRival.Banner("You have no cards left to draw!  Deck out!");
        await Task.Delay(900);
        // a Free Duel or the Duel Master K practice: lost, with its results (not a game over)
        if (FmRestDuel.Active) { await FmRestDuel.OnLoss(p); return; }
        if (FmPractice.Active)
        {
            int turns = p.Creature.CombatState?.RoundNumber ?? 1;
            int comLp = Math.Max(0, FmRival.Priest?.CurrentHp ?? 0);
            FmRival.Active = false;
            await FmPractice.Finish(false, p, comLp, turns);
            return;
        }
        await CreatureCmd.Kill(p.Creature, true);
    }

    /// <summary>Before a duel: DUEL, or BUILD DECK first.</summary>
    public static async Task BeforeDuel(Player p)
    {
        if (await FmScene.Open() is not { } s) return;
        int pick;
        try
        {
            s.HideSkip();
            pick = await s.Menu("DUEL", "BUILD DECK");
        }
        finally { await s.Close(); }
        if (pick == 1) await FmDeckEditor.Open(p);
    }
}

/// <summary>Forbidden Memories: the used cards don't come back into the deck during a duel.</summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CardPileCmd), nameof(MegaCrit.Sts2.Core.Commands.CardPileCmd.ShuffleIfNecessary))]
static class FmNoReshufflePatch
{
    static bool Prefix(Player player, ref Task __result)
    {
        if (!FmRules.IsFmPlayer(player)) return true; // every duel, the Duel Master K practice too
        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>...and no direct reshuffle either (a running-out draw shuffles the discard back in through this one).</summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CardPileCmd), nameof(MegaCrit.Sts2.Core.Commands.CardPileCmd.Shuffle))]
static class FmNoShufflePatch
{
    static bool Prefix(Player player, ref Task __result)
    {
        if (!FmRules.IsFmPlayer(player)) return true; // every duel, the Duel Master K practice too
        __result = Task.CompletedTask;
        return false;
    }
}
