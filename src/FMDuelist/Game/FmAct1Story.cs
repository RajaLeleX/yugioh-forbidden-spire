using MegaCrit.Sts2.Core.Combat;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Powers;

namespace FMDuelist.Game;

/// <summary>
/// The end of Act 1, retold from Forbidden Memories' own script (read from the player's disc):
///  - before the boss: the festival in the town plaza, Seto beating Jono, then the wait at the duel ground;
///  - after beating Young Priest Seto: his farewell, Jono and Teana celebrating, then Heishin's invasion of the palace;
///  - the unwinnable duel with High Mage Heishin (his first-duel deck, the Umi field, track 35). Losing it doesn't end
///    the run: your Life Points are restored;
///  - Simon Muran begs you to shatter the Puzzle (refusing only loops back), you wake inside it, Simon gives you the
///    last of his power (a Bright Castle card reward), then "coming soon" for Acts 2 and 3.
/// The duels with Jono and Teana are left out entirely.
/// </summary>
static partial class FmAct1Story
{
    const string Name = "K";
    public const int BrightCastle = 668, Umi = 334;
    const int PlazaTrack = 52, DuelGroundTrack = 15, PalaceTrack = 40, HeishinTrack = 27, SetoNightTrack = 24, LossTrack = 26, PuzzleTrack = 5, HeishinDuelTrack = 35;

    public static readonly FmRival.Duelist Heishin = new(typeof(TheKinBoss), typeof(FmHeishin), "High Mage Heishin", 8, 8000, 0);

    public enum Phase { None, HeishinPending, HeishinDuel, Lost }
    public static Phase State;
    static int _savedHp, _startRound;
    static Vector2? _setoSpot;

    public static void Reset() { State = Phase.None; _setoSpot = null; }

    public static bool Enabled(Player p) => FmRules.IsFmPlayer(p) && !FmPractice.Active && p.RunState.CurrentActIndex == 0;
    public static bool IsHeishin(Creature? c) => c?.Monster is FmHeishin;

    /// <summary>A story line from the player's disc (empty without the disc).</summary>
    public static string Line(string id) => FmData.Slus is { } s && At.TryGetValue(id, out var o) ? FmText.ReadStory(s, o) : "";

    static async Task<int> Choose(FmScene s, int proceed, params string[] opts) => s.Skipped ? proceed : await s.Choose(opts);

    static void Music(int track) { try { FmMusic.Play(track); } catch (Exception ex) { Log.Warn("[FMDuelist] story music: " + ex.Message); } }

    // ---------------- before the boss ----------------
    public static async Task PreBoss()
    {
        if (await FmScene.Open() is not { } s) return;
        try
        {
            Music(PlazaTrack);
            await s.Show("act1_plaza_teana");
            await s.Say("tea_lots"); await s.Say("tea_mages");
            await s.Show("act1_procession");
            await s.Say("tea_awesome"); await s.Say("tea_spooky"); await s.Say("tea_heishin");
            await s.Say("tea_kid"); await s.Say("tea_attitude"); await s.Say("tea_world");
            await s.Hide();
            await s.Say("tea_lookjono");
            await s.Show("act1_jono_seto");
            await s.Say("jono_lost"); await s.Say("seto_speck"); await s.Say("jono_saywhat"); await s.Say("seto_forgive");
            await s.Show("act1_jono_plaza");
            await s.Say("tea_heyjono"); await s.Say($"Teana and {Name}...", true);
            await s.Say("tea_whatsup"); await s.Say("jono_whaddaya"); await s.Say("tea_youlost"); await s.Say("jono_blunt");
            await s.Show("act1_seto_plaza");
            await s.Say("seto_weary"); await s.Say("seto_worthy"); await s.Say("jono_nerve");
            await s.Say($"Hey, {Name}...\nWhy don't you challenge him?", true);
            await s.Say("tea_afterall"); await s.Say("tea_bet");
            await s.Say($"Yeah, {Name}!\nDo it! Show him who's\nboss around here!", true);
            await s.Say("seto_victim"); await s.Say("seto_hope");
            await s.Say("guard_calls"); await s.Say("seto_isee"); await s.Say("seto_postponed");
            await s.Say("seto_frequent"); await s.Say("jono_right"); await s.Say("seto_waitthere"); await s.Say("seto_name");
            await s.Show("act1_jono_plaza");
            await s.Say("jono_believe"); await s.Say("tea_charmer");
            await s.Say($"I got a feeling {Name}\nwill put him in his place.\nWon't ya, buddy?", true);
            await s.Hide(0.8);
            // the duel ground
            Music(DuelGroundTrack);
            await s.Show("act1_jono_duelground");
            await s.Say("jono_wait"); await s.Say("jono_tough"); await s.Say("jono_listen"); await s.Say("jono_fact");
            await s.Say("jono_butmage"); await s.Say("jono_chance");
            await s.Say($"Listen, {Name}...\nDon't underestimate the guy.", true);
            await s.Say("jono_sloppy"); await s.Say("jono_there"); await s.Say("jono_lookat"); await s.Say("jono_groupies"); await s.Say("jono_whip");
            await s.Show("act1_seto_lost");
            await s.Say("seto_entertain"); await s.Say("seto_enjoy"); await s.Say("seto_disappoint");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 pre-boss scene: " + ex); }
        await s.Close();
    }

    // ---------------- after beating Young Priest Seto ----------------
    /// <summary>Runs before Seto falls: results, his farewell, the celebration and Heishin's invasion. Heishin joins the
    /// field right away, so the combat carries on into his duel once Seto is gone.</summary>
    public static async Task AfterSeto(Player player, int turns)
    {
        await FmDuelEnd.Show(true, player, 0, FmRival.Current?.Name ?? "Young Priest Seto", turns);
        if (FmRival.Priest != null && NCombatRoom.Instance?.GetCreatureNode(FmRival.Priest) is { } pn) _setoSpot = pn.GlobalPosition;
        if (await FmScene.Open() is { } s)
        {
            try
            {
                Music(DuelGroundTrack);
                await s.Show("act1_seto_lost");
                await s.Say("seto_cannot"); await s.Say("seto_wait"); await s.Say("seto_hmm"); await s.Say("seto_enough");
                await s.Say("seto_paths"); await s.Say("seto_farewell");
                await s.Show("act1_jono_teana");
                await s.Say($"You did it! {Name}!", true); await s.Say("jono_didja");
                await s.Say($"{Name}...\nYou're just too good!", true);
                await s.Say("\"I bid you farewell\"...\nHah! What a loser!", true);
                await s.Hide(1.2);
                // the palace that night
                Music(PalaceTrack);
                await s.Show("act1_palace_night", 0.8);
                await s.Wait(1.0);
                await s.Show("act1_simon_night");
                await s.Say("simon_room"); await s.Say("simon_ready");
                await s.Show("act1_simon_study");
                await s.Say("simon_annoy"); await s.Say("serv_trouble"); await s.Say("serv_mage"); await s.Say("serv_power");
                await s.Say("simon_what"); await s.Say("serv_sorcery"); await s.Say("simon_where"); await s.Say("serv_hall"); await s.Say("simon_dog");
                Music(HeishinTrack);
                await s.Show("act1_ruined_hall");
                await s.Say("simon_whatthe");
                await s.Show("act1_heishin_ruins");
                await s.Say("hei_goodsee"); await s.Say("simon_revolt"); await s.Say("hei_mad"); await s.Say("hei_throne");
                await s.Say("simon_throne"); await s.Say("hei_power"); await s.Say("simon_power"); await s.Say("hei_recog");
                await s.Say("simon_dark"); await s.Say("hei_treasure");
                await s.Show("act1_heishin_rod");
                await s.Say("hei_taste");
                await s.Flash();
                await s.Show("act1_palace_hall");
                await s.Show("act1_servant");
                await s.Say($"Prince {Name}!", true); await s.Say("isis_invaded"); await s.Say("isis_leave");
                await s.Hide();
                await s.Show("act1_seto_men");
                await s.Say("isis_no");
                await s.Say($"Prince {Name}...\nDid you hope to escape me?", true);
                await s.Say("seto_meet"); await s.Say("seto_away"); await s.Say("seto_speak"); await s.Say("guard_cmd");
                Music(SetoNightTrack);
                await s.Show("act1_seto_night");
                await s.Say($"Why do you flee?\n{Name}.", true);
                await s.Say("seto_captive"); await s.Say("seto_provoke"); await s.Say("seto_want"); await s.Say("seto_iwant");
                await s.Say("seto_location"); await s.Say("seto_spare"); await s.Say("seto_tellme");
                int tell = await Choose(s, 0, "Refuse", "Tell");
                await s.Say(tell == 0 ? "seto_fool" : "seto_talk");
                await s.Say("simon_myprince"); await s.Say("seto_simon");
                await s.Show("act1_simon_puzzle");
                await s.Say("simon_item"); await s.Say("simon_takeit");
                await s.Say($"{Name} gets Millennium Puzzle!", true);
                await s.Show("act1_simon_outside");
                await s.Say("simon_keep"); await s.Say("simon_key"); await s.Say("simon_wicked");
                await s.Say("seto_wellwell"); await s.Say("seto_handkey"); await s.Say("simon_cur"); await s.Say("simon_leave");
                await s.Say("seto_outway"); await s.Say("simon_run");
                if (await Choose(s, 0, "Run", "Stay") == 1) await s.Say("simon_listen");
                Music(HeishinTrack);
                await s.Show("act1_heishin_night");
                await s.Say("hei_donotmove"); await s.Say("hei_found"); await s.Say("seto_yes"); await s.Say("hei_escape");
                await s.Say("hei_resist"); await s.Say("hei_handover");
                await s.Show("act1_simon_outside");
                await s.Say("simon_option"); await s.Say("simon_battle");
                // FM keeps asking until you agree to the duel
                while (await Choose(s, 0, "Duel", "Pass") == 1)
                {
                    await s.Say("simon_confront"); await s.Say("simon_unleash"); await s.Say("simon_face");
                }
                await s.Show("act1_heishin_night");
                await s.Say("hei_wasted");
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 ending scene: " + ex); }
            await s.Close();
        }
        // Heishin takes the field now, so the combat goes on when Seto falls
        try
        {
            if (player.Creature.CombatState is { } combat)
            {
                var h = await CreatureCmd.Add(ModelDb.Monster<FmHeishin>().ToMutable(), combat, CombatSide.Enemy, null);
                if (_setoSpot is { } spot && NCombatRoom.Instance?.GetCreatureNode(h) is { } hn) hn.GlobalPosition = spot;
                State = Phase.HeishinPending;
            }
        }
        catch (Exception ex) { Log.Error("[FMDuelist] Heishin join: " + ex); State = Phase.None; }
    }

    /// <summary>Seto is gone: the duel with Heishin begins (his deck, the Umi field, track 35).</summary>
    public static async Task StartHeishin(PlayerChoiceContext ctx, Player player)
    {
        if (State != Phase.HeishinPending || player.Creature.CombatState is not { } combat) return;
        State = Phase.HeishinDuel;
        _savedHp = player.Creature.CurrentHp;
        _startRound = combat.RoundNumber;
        // a fresh field for the new duel
        foreach (var z in FmField.Living(player).ToList()) z.Clear();
        FmField.ResetCombat();
        await FmRival.Begin(player, combat);
        Music(HeishinDuelTrack);
        FmRival.Banner("High Mage Heishin challenges you!");
        // Heishin's field: Umi
        try
        {
            if (Activator.CreateInstance(FmSheet.TypeFor(Umi)) is FmSpellCard umi)
            {
                FmField.FieldBoost.Clear();
                foreach (var b in umi.BoostTypes) FmField.FieldBoost.Add(b);
                await PowerCmd.Remove<FmFieldPower>(player.Creature);
                await PowerCmd.Apply<FmFieldPower>(ctx, player.Creature, Umi, player.Creature, null);
                FmBackdrop.Set(Umi);
                FmField.NotifyChanged();
                await Task.Delay(900);
                FmRival.Banner("High Mage Heishin plays Umi!");
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] Umi: " + ex.Message); }
    }

    public static int TurnsIntoDuel(ICombatState? c) => Math.Max(1, (c?.RoundNumber ?? 1) - _startRound + 1);

    // ---------------- the unwinnable duel is lost ----------------
    public static async Task AfterHeishin(Player player)
    {
        State = Phase.Lost;
        FmRival.Active = false;
        int turns = TurnsIntoDuel(player.Creature.CombatState);
        int comLp = Math.Max(1, FmRival.Priest?.CurrentHp ?? 8000);
        await FmDuelEnd.Show(false, player, comLp, Heishin.Name, turns, spoils: false);
        // not a game over: the duelist is back as he was before the duel
        try { await FmRules.Raw(() => CreatureCmd.SetCurrentHp(player.Creature, Math.Max(1, _savedHp))); } catch (Exception ex) { Log.Warn("[FMDuelist] restore LP: " + ex.Message); }
        if (await FmScene.Open() is not { } s) return;
        try
        {
            Music(LossTrack);
            await s.Show("act1_heishin_night");
            await s.Say("hei_foolish"); await s.Say("hei_rip");
            await s.Show("act1_heishin_simon");
            await s.Say("hei_orelse"); await s.Say("simon_shatter"); await s.Say("simon_vile"); await s.Say("hei_hands");
            await s.Say("simon_puzzle"); await s.Say("simon_dontgive");
            // Simon won't take no for an answer
            while (await Choose(s, 1, "Don't shatter the puzzle", "Shatter the puzzle") == 0)
                await s.Say("But Prince.....\nThe Puzzle", true);
            await s.Show("act1_puzzle_shatter", 0.2);
            await s.Say("hei_no");
            await s.Flash();
            Music(PuzzleTrack);
            await s.Show("act1_inside_puzzle", 1.0);
            await s.Wait(1.0);
            await s.Show("act1_simon_inside");
            await s.Say("simon_awoken"); await s.Say("simon_precise"); await s.Say("simon_sorry"); await s.Say("simon_allow");
            await s.Say("simon_destroy"); await s.Say("simon_oneday"); await s.Say("simon_sleep"); await s.Say("simon_someone");
            await s.Say("simon_guide");
            await s.Say("I won't be able to reach you\nwhen you wake up, but please\ntake the remainder of my power.", true);
            // Simon's gift: a Bright Castle, picked up like a card reward
            s.SetVisible(false);
            await GiveBrightCastle(player);
            s.SetVisible(true);
            await s.Say("simon_rest");
            await s.Caption("World Tournament and Final Arc (Act 2 and 3) coming soon, for now Acts 2 and 3 will not include cutscenes but battles can still be played till the end! Good luck!", 2.0);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 puzzle scene: " + ex); }
        await s.Close();
        // the story is over: Heishin and his zones leave, and the boss rewards follow as usual
        TaskHelper.RunSafely(EndCombat(player));
    }

    static async Task GiveBrightCastle(Player player)
    {
        try
        {
            var card = player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(BrightCastle))), player);
            var ctx = new BlockingPlayerChoiceContext();
            var pick = await MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromChooseACardScreen(ctx, new[] { card }, player, canSkip: false) ?? card;
            await CardPileCmd.Add(pick, PileType.Deck);
            Log.Info("[FMDuelist] Simon's gift: Bright Castle added to the deck");
        }
        catch (Exception ex)
        {
            Log.Warn("[FMDuelist] Bright Castle reward screen: " + ex.Message);
            try { await CardPileCmd.Add(player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(BrightCastle))), player), PileType.Deck); }
            catch (Exception ex2) { Log.Warn("[FMDuelist] Bright Castle: " + ex2.Message); }
        }
    }

    static async Task EndCombat(Player player)
    {
        await Task.Delay(300);
        State = Phase.None;
        try
        {
            if (player.Creature.CombatState is { } combat)
                foreach (var e in combat.Enemies.Where(e => e.IsAlive).ToList()) await CreatureCmd.Kill(e, force: true);
        }
        catch (Exception ex) { Log.Error("[FMDuelist] end story combat: " + ex); }
        FmRival.Reset();
    }
}

/// <summary>High Mage Heishin: a dark sorcerer standing in for him (The Forgotten's look).</summary>
public sealed class FmHeishin : MonsterModel
{
    public override int MinInitialHp => 80;
    public override int MaxInitialHp => 80;
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/the_forgotten");

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var idle = new MoveState("FM_DUEL_MOVE", (IReadOnlyList<Creature> _) => Task.CompletedTask);
        idle.FollowUpState = idle;
        return new MonsterMoveStateMachine(new List<MonsterState> { idle }, idle);
    }

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        var idle = new AnimState("idle_loop", isLooping: true);
        var hurt = new AnimState("hurt"); hurt.NextState = idle;
        var attack = new AnimState("attack"); attack.NextState = idle;
        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState("Dead", new AnimState("die"));
        animator.AddAnyState("Hit", hurt);
        animator.AddAnyState("Attack", attack);
        return animator;
    }
}
