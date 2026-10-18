using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Relics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace FMDuelist.Game;

/// <summary>
/// Free Duels at rest sites (in place of Cut): duel any duelist you've already beaten, up to 5 times per rest site,
/// for a card from their real Forbidden Memories drop table (the table for the rank you earn; the Millennium Items
/// improve the odds). Your LP is 8000 for the duel, a loss isn't a game over (you're back at the rest site, no drop,
/// one attempt used), and afterwards your HP is what it was before. Resting still works as normal.
/// </summary>
public static class FmRestDuel
{
    public const int Attempts = 5;

    /// <summary>A Free Duel from a rest site is on (its rewards, music and lines are its own).</summary>
    public static bool Active { get; private set; }
    static FmRival.Duelist? _foe;
    static int _hpBefore;
    static bool _lost;

    public static bool Lost => Active && _lost;

    /// <summary>Duelists you've beaten (FM duelist numbers), saved with the run on the Millennium Puzzle.</summary>
    public static IEnumerable<FmRival.Duelist> Beaten(Player p)
    {
        var ids = p.GetRelic<MillenniumPuzzle>()?.BeatenDuelists ?? Array.Empty<int>();
        return FmRival.AllDuelists.Where(d => d.DuelistIndex > 0 && d != FmAct1Story.Heishin && ids.Contains(d.DuelistIndex))
            .GroupBy(d => d.DuelistIndex).Select(g => g.First());
    }

    public static void MarkBeaten(Player p, FmRival.Duelist d)
    {
        if (d.DuelistIndex <= 0 || d == FmAct1Story.Heishin || p.GetRelic<MillenniumPuzzle>() is not { } pz) return;
        if (pz.BeatenDuelists.Contains(d.DuelistIndex)) return;
        pz.BeatenDuelists = pz.BeatenDuelists.Append(d.DuelistIndex).ToArray();
    }

    /// <summary>Free Duels left at this rest site (5 per visit; kept with the run).</summary>
    public static int Left(Player p)
    {
        if (p.GetRelic<MillenniumPuzzle>() is not { } pz) return 0;
        int floor = p.RunState.TotalFloor;
        if (pz.RestDuels.Length != 2 || pz.RestDuels[0] != floor) pz.RestDuels = new[] { floor, Attempts };
        return pz.RestDuels[1];
    }

    static void UseOne(Player p)
    {
        if (p.GetRelic<MillenniumPuzzle>() is { } pz && pz.RestDuels.Length == 2) pz.RestDuels = new[] { pz.RestDuels[0], Math.Max(0, pz.RestDuels[1] - 1) };
    }

    // ---------------- picking and starting ----------------
    public static async Task Choose(Player player)
    {
        var foes = Beaten(player).ToList();
        if (foes.Count == 0 || Left(player) <= 0) return;
        if (await FmScene.Open() is not { } s) return;
        int pick;
        try
        {
            s.HideSkip();
            await s.Bg("bg_room");
            await s.Lit("k", $"Free Duel: {Left(player)} left here.\nWho will you duel?");
            var names = foes.Select(f => f.Name).Append("Back").ToList();
            var infos = foes.Select(f => $"{f.Name}\nWin to roll a card from {f.Name}'s own drop table. Your rank decides the table: POW ranks, TEC ranks, or B/C/D. LP is 8000; a loss costs only the attempt.")
                .Append("Back to the rest site.").ToList();
            pick = await s.ChooseInfo(names, infos, "FREE DUEL");
        }
        finally { await s.Close(); }
        if (pick < 0 || pick >= foes.Count) return;
        await Start(player, foes[pick]);
    }

    static async Task Start(Player player, FmRival.Duelist foe)
    {
        UseOne(player);
        Active = true; _foe = foe; _lost = false;
        _hpBefore = player.Creature.CurrentHp;
        Log.Info($"[FMDuelist] Free Duel at the rest site vs {foe.Name} (#{foe.DuelistIndex}), {Left(player)} left");
        try
        {
            var encounter = ((EncounterModel)ModelDb.GetById<EncounterModel>(ModelDb.GetId(foe.Encounter))).ToMutable();
            var state = player.RunState;
            var combat = new CombatState(encounter, state, state.Modifiers, state.BadgeModels, state.MultiplayerScalingModel);
            var room = new CombatRoom(combat) { ShouldCreateCombat = true, ShouldResumeParentEventAfterCombat = true };
            await RunManager.Instance.EnterRoomWithoutExitingCurrentRoom(room, true);
        }
        catch (Exception ex) { Log.Error("[FMDuelist] free duel start: " + ex); Active = false; }
    }

    /// <summary>At the start of the duel: 8000 LP for both.</summary>
    public static async Task Begin(Player player)
    {
        if (!Active) return;
        await FmRules.Raw(() => CreatureCmd.SetCurrentHp(player.Creature, Math.Min(player.Creature.MaxHp, 8000)));
    }

    /// <summary>You fell: the results, then the duel simply ends (no game over).</summary>
    public static async Task OnLoss(Player player)
    {
        _lost = true;
        FmRival.Active = false;
        int turns = player.Creature.CombatState?.RoundNumber ?? 1;
        int comLp = Math.Max(1, FmRival.Priest?.CurrentHp ?? 8000);
        await FmDuelEnd.Show(false, player, comLp, _foe?.Name ?? "COM", turns, spoils: false);
        await FmRules.Raw(() => CreatureCmd.SetCurrentHp(player.Creature, Math.Max(1, _hpBefore)));
        TaskHelper.RunSafely(EndCombat(player));
    }

    static async Task EndCombat(Player player)
    {
        await Task.Delay(300);
        try
        {
            if (player.Creature.CombatState is { } combat)
                foreach (var e in combat.Enemies.Where(e => e.IsAlive).ToList()) await CreatureCmd.Kill(e, force: true);
        }
        catch (Exception ex) { Log.Error("[FMDuelist] end free duel: " + ex); }
        FmRival.Reset();
    }

    /// <summary>The duel's rewards: only the drop (and none after a loss).</summary>
    public static bool Rewards(Player player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!Active || room is not CombatRoom) return false;
        rewards.Clear();
        if (!_lost && _foe != null && _rank != null)
        {
            var cards = Drops(player, _foe, _rank.Value).Select(id => player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player)).ToList();
            if (cards.Count > 0)
            {
                var opts = new CardCreationOptions(cards, CardCreationSource.Encounter, CardRarityOddsType.Uniform);
                rewards.Add(new CardReward(cards, CardCreationSource.Encounter, player, opts));
            }
        }
        return true;
    }

    /// <summary>After the duel: your HP is what it was before you came to duel.</summary>
    public static async Task AfterCombat(Player player)
    {
        if (!Active) return;
        try { await FmRules.Raw(() => CreatureCmd.SetCurrentHp(player.Creature, Math.Max(1, _hpBefore))); } catch (Exception ex) { Log.Warn("[FMDuelist] free duel hp: " + ex.Message); }
    }

    /// <summary>Back at the rest site.</summary>
    public static void Finished() { Active = false; _foe = null; _lost = false; _rank = null; }

    // ---------------- drops ----------------
    public enum Table { SaPow = 1, Bcd = 2, SaTec = 3 }
    static Table? _rank;

    /// <summary>Remembers the rank you won with (FM: S/A POW, S/A TEC, or B/C/D).</summary>
    public static void Ranked(string kind, string letter) =>
        _rank = letter is "S" or "A" ? (kind == "POW" ? Table.SaPow : Table.SaTec) : Table.Bcd;

    static readonly Dictionary<(int, Table), int[]> _tables = new();

    /// <summary>A duelist's drop table from the disc: 722 weights after their deck (they sum to 2048).</summary>
    static int[] Weights(int duelistIndex, Table table)
    {
        if (_tables.TryGetValue((duelistIndex, table), out var w)) return w;
        w = new int[FmData.CardCount];
        var wa = FmData.Wa;
        if (wa != null)
        {
            int o = 0xE9B000 + (duelistIndex - 1) * 0x1800 + (int)table * 0x5B4;
            for (int k = 0; k < FmData.CardCount && o + k * 2 + 1 < wa.Length; k++) w[k] = BitConverter.ToUInt16(wa, o + k * 2);
        }
        return _tables[(duelistIndex, table)] = w;
    }

    static readonly Random _rng = new();

    static int Roll(int[] w, Func<int, bool>? only = null)
    {
        int total = 0;
        for (int k = 0; k < w.Length; k++) if (w[k] > 0 && (only == null || only(k + 1))) total += w[k];
        if (total <= 0) return 0;
        int r = _rng.Next(total);
        for (int k = 0; k < w.Length; k++)
        {
            if (w[k] <= 0 || (only != null && !only(k + 1))) continue;
            r -= w[k];
            if (r < 0) return k + 1;
        }
        return 0;
    }

    static bool Usable(int id) => id > 0 && FmData.Get(id) is { Name.Length: > 0 } && FmSheet.TypeFor(id) != null;

    /// <summary>
    /// The drop, FM style, with the Millennium Items on top: the Ring may reroll for a stronger card, the Rod for a
    /// Dragon, the Scale for a Magic/Equip/Trap card (only cards that are in this duelist's table); the Eye rolls a
    /// second card to choose between; the Necklace adds a second drop.
    /// </summary>
    static List<int> Drops(Player player, FmRival.Duelist foe, Table table)
    {
        var w = Weights(foe.DuelistIndex, table);
        int One()
        {
            int id = Roll(w);
            if (player.GetRelic<FmItemRing>() != null && _rng.NextDouble() < FmItemRing.Chance)
            {
                int other = Roll(w);
                if (Usable(other) && FmData.Get(other).Atk > FmData.Get(id).Atk) id = other;
            }
            if (player.GetRelic<FmItemRod>() != null && _rng.NextDouble() < FmItemRod.Chance && Roll(w, k => FmData.Get(k) is { IsMonster: true, Type: FmType.Dragon }) is int d and > 0) id = d;
            if (player.GetRelic<FmItemScale>() != null && _rng.NextDouble() < FmItemScale.Chance && Roll(w, k => FmData.Get(k).Type is FmType.Magic or FmType.Equip or FmType.Trap) is int m and > 0) id = m;
            return id;
        }
        var drops = new List<int> { One() };
        if (player.GetRelic<FmItemEye>() != null) drops.Add(One());
        if (player.GetRelic<FmItemNecklace>() != null) drops.Add(One());
        var result = drops.Where(Usable).Distinct().ToList();
        Log.Info($"[FMDuelist] Free Duel drop vs {foe.Name} ({table}): {string.Join(", ", result.Select(i => FmData.Get(i).Name))}");
        return result;
    }

    /// <summary>Checks the drop tables read sensibly from the disc (each sums to 2048 in FM).</summary>
    public static void CheckTables()
    {
        try
        {
            foreach (var t in new[] { Table.SaPow, Table.Bcd, Table.SaTec })
                Log.Info($"[FMDuelist] drop table check, Simon {t}: sum {Weights(1, t).Sum()}");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] drop tables: " + ex.Message); }
    }
}

/// <summary>The rest site's Free Duel option (in place of Cut).</summary>
public sealed class FmFreeDuelRestSiteOption : RestSiteOption
{
    public FmFreeDuelRestSiteOption(Player owner) : base(owner) { }

    public override string OptionId => "FMFREEDUEL";
    public override IEnumerable<string> AssetPaths => new[] { FmCutRestSiteOption.SmithIcon };
    public override bool IsEnabled => FmRestDuel.Left(Owner) > 0 && FmRestDuel.Beaten(Owner).Any();

    public override async Task<bool> OnSelect()
    {
        // the rest site stays open: duels don't use up the rest
        TaskHelper.RunSafely(FmRestDuel.Choose(Owner));
        await Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Icon), MethodType.Getter)]
static class FreeDuelIconPatch
{
    static bool Prefix(RestSiteOption __instance, ref Texture2D __result)
    {
        if (__instance is not FmFreeDuelRestSiteOption) return true;
        __result = PreloadManager.Cache.GetTexture2D(FmCutRestSiteOption.SmithIcon);
        return false;
    }
}

/// <summary>Back at the rest site after a Free Duel.</summary>
[HarmonyPatch(typeof(RestSiteRoom), nameof(RestSiteRoom.Resume))]
static class FreeDuelResumePatch
{
    static void Postfix() => FmRestDuel.Finished();
}
