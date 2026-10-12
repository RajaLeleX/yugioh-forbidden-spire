using MegaCrit.Sts2.Core.Combat;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Powers;
using FMDuelist.Relics;

namespace FMDuelist.Game;

/// <summary>
/// Act 1's story, retold from Forbidden Memories' own script (read from the player's disc):
///  - the start: Simon Muran scolds the prince and the prince runs away (before the starting choice);
///  - each story duel opens with that duelist's lines at the duel ground (Simon at the palace) and closes with theirs;
///  - the festival "?" point: the town plaza and Seto beating Jono;
///  - before the boss: waiting for Seto at the duel ground;
///  - after the boss's rewards: Seto's farewell, Jono and Teana celebrating, Heishin's invasion, then a secret level:
///    the unwinnable duel with High Mage Heishin (his first-duel deck, the Umi field, track 35);
///  - losing it isn't a game over: Simon has you shatter the Puzzle (refusing only loops back), you wake inside it,
///    Simon's gift (Bright Castle) is the reward, then "coming soon" and Act 2.
/// </summary>
static partial class FmAct1Story
{
    const string Name = "K";
    public const int BrightCastle = 668, Umi = 334;
    const int PlazaTrack = 52, DuelGroundTrack = 15, PalaceTrack = 40, HeishinTrack = 27, SetoNightTrack = 24, LossTrack = 26, PuzzleTrack = 5, HeishinDuelTrack = 35;

    public static readonly FmRival.Duelist Heishin = new(typeof(FmHeishinEncounter), typeof(FmHeishin), "High Mage Heishin", 8, 8000, 0);

    public enum Phase { None, HeishinDuel, Lost }
    public static Phase State;
    static int _startRound;

    /// <summary>How far the Act 1 ending has got (saved with the run on the Millennium Puzzle).</summary>
    public const int StageNone = 0, StageHeishin = 1, StagePuzzleDone = 2, StageDone = 3;

    public static void Reset() { State = Phase.None; }

    public static bool Enabled(Player p) => FmRules.IsFmPlayer(p) && !FmPractice.Active && p.RunState.CurrentActIndex == 0;
    public static bool IsHeishin(Creature? c) => c?.Monster is FmHeishin;

    /// <summary>A story line from the player's disc (empty without the disc).</summary>
    public static string Line(string id) => FmData.Slus is { } s && At.TryGetValue(id, out var o) ? FmText.ReadStory(s, o) : "";

    static async Task<int> Choose(FmScene s, int proceed, params string[] opts) => s.Skipped ? proceed : await s.Choose(opts);

    static void Music(int track) { try { FmMusic.Play(track); } catch (Exception ex) { Log.Warn("[FMDuelist] story music: " + ex.Message); } }

    static MillenniumPuzzle? Puzzle(Player p) => p.GetRelic<MillenniumPuzzle>();

    // ---------------- the start: Simon scolds the prince, who runs away ----------------
    public static async Task SimonScolds()
    {
        while (FmStoryIntro.Playing) await Task.Delay(100);
        if (await FmScene.Open() is not { } s) return;
        try
        {
            Music(PalaceTrack);
            await s.Bg("bg_palace"); await s.Chr("chr_simon_palace", "simon");
            await s.Say("sm_dear"); await s.Say("sm_royal"); await s.Say("sm_shame");
            // the prince runs away
            await s.Lit("k", "(You run away!)");
            await s.Say("sm_stop");
            await s.Chr(null);
            await s.Say("sm_drat");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] Simon's scolding: " + ex); }
        await s.Close();
    }

    // ---------------- the story duels ----------------
    public static async Task PreDuel(FmStoryAct1.StoryDuel d)
    {
        if (await FmScene.Open() is not { } s) return;
        try
        {
            Music(d.Key == "simon" ? PalaceTrack : DuelGroundTrack);
            await s.Bg(d.Bg); await s.Chr(d.Chr, d.Key);
            switch (d.Key)
            {
                case "simon":
                    await s.Lit("simon", $"Prince {Name}.\nYou've returned...");
                    await s.Say("sm_night"); await s.Say("sm_ifplay"); await s.Say("sm_tryhand"); await s.Say("sm_iflose"); await s.Say("sm_worthy");
                    await s.Say("sm_hoho"); await s.Say("sm_dontthink"); await s.Say("sm_prepare");
                    break;
                case "teana":
                    await s.Say("te_waiting"); await s.Say("te_jono"); await s.Say("te_duel"); await s.Say("te_play");
                    break;
                case "v1":
                    await s.Say("v1_fest"); await s.Say("v1_rather"); await s.Say("v1_takeme"); await s.Say("v1_play");
                    break;
                case "v2":
                    await s.Say("v2_rare"); await s.Say("v2_pretty"); await s.Say("v2_beat"); await s.Say("v2_lose"); await s.Say("v2_play");
                    break;
                case "v3":
                    await s.Say("v3_talk"); await s.Say("v3_unless"); await s.Say("v3_play");
                    break;
                case "jono":
                    await s.Say("jo_geez"); await s.Say("jo_admit");
                    await s.Lit("jono", $"Hey, {Name},\nHow about a game until\nthat guy comes back?");
                    await s.Say("jo_letsdo"); await s.Say("jo_lostlast");
                    break;
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] pre-duel scene: " + ex); }
        await s.Close();
    }

    public static async Task PostDuel(FmStoryAct1.StoryDuel d)
    {
        if (await FmScene.Open() is not { } s) return;
        try
        {
            await s.Bg(d.Bg); await s.Chr(d.Chr, d.Key);
            switch (d.Key)
            {
                case "simon": await s.Say("sm_won"); await s.Say("sm_taught"); await s.Say("sm_fair"); break;
                case "teana": await s.Say("te_ouch"); await s.Say("te_again"); break;
                case "v1": await s.Say("v1_wellwell"); break;
                case "v2": await s.Say("v2_owe"); break;
                case "v3":
                    await s.Say("v3_lost"); await s.Say("v3_day");
                    // Teana hears about the festival
                    await s.Chr("chr_teana_room", "teana");
                    await s.Say("te_fest"); await s.Say("te_checkout"); await s.Say("te_findjono");
                    break;
                case "jono":
                    await s.Lit("jono", $"Geez, {Name}!\nYou whipped me!\nY'know... you are good!");
                    await s.Say("jo_favor");
                    break;
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] after-duel scene: " + ex); }
        await s.Close();
    }

    // ---------------- the festival ("?" point) ----------------
    public static async Task Festival()
    {
        if (await FmScene.Open() is not { } s) return;
        try
        {
            Music(PlazaTrack);
            await s.Still("act1_plaza_teana", "teana");
            await s.Say("tea_lots"); await s.Say("tea_mages");
            await s.Still("act1_procession");
            await s.Say("tea_awesome"); await s.Say("tea_spooky"); await s.Say("tea_heishin");
            await s.Say("tea_kid"); await s.Say("tea_attitude"); await s.Say("tea_world");
            await s.Hide();
            await s.Say("tea_lookjono");
            await s.Still("act1_jono_seto", "jono", "seto");
            await s.Say("jono_lost"); await s.Say("seto_speck"); await s.Say("jono_saywhat"); await s.Say("seto_forgive");
            await s.Bg("bg_cols"); await s.Chr("chr_jono_cols", "jono");
            await s.Say("tea_heyjono"); await s.Lit("jono", $"Teana and {Name}...");
            await s.Say("tea_whatsup"); await s.Say("jono_whaddaya"); await s.Say("tea_youlost"); await s.Say("jono_blunt");
            await s.Chr("chr_seto_cols", "seto");
            await s.Say("seto_weary"); await s.Say("seto_worthy"); await s.Say("jono_nerve");
            await s.Lit("teana", $"Hey, {Name}...\nWhy don't you challenge him?");
            await s.Say("tea_afterall"); await s.Say("tea_bet");
            await s.Lit("jono", $"Yeah, {Name}!\nDo it! Show him who's\nboss around here!");
            await s.Say("seto_victim"); await s.Say("seto_hope");
            await s.Say("guard_calls"); await s.Say("seto_isee"); await s.Say("seto_postponed");
            await s.Say("seto_frequent"); await s.Say("jono_right"); await s.Say("seto_waitthere"); await s.Say("seto_name");
            await s.Chr("chr_jono_cols", "jono");
            await s.Say("jono_believe"); await s.Say("tea_charmer");
            await s.Lit("jono", $"I got a feeling {Name}\nwill put him in his place.\nWon't ya, buddy?");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] festival scene: " + ex); }
        await s.Close();
        try { if (RunManager.Instance.DebugOnlyGetState() is { } st && st.CurrentRoom is { } r && st.Players.FirstOrDefault() is { } p) FmMusic.OnRoomEntered(r, p); } catch { }
    }

    // ---------------- before the boss: waiting at the duel ground ----------------
    public static async Task PreBoss()
    {
        if (await FmScene.Open() is not { } s) return;
        try
        {
            Music(DuelGroundTrack);
            await s.Bg("bg_room"); await s.Chr("chr_jono_cols", "jono");
            await s.Say("jono_wait"); await s.Say("jono_tough"); await s.Say("jono_listen"); await s.Say("jono_fact");
            await s.Say("jono_butmage"); await s.Say("jono_chance");
            await s.Lit("jono", $"Listen, {Name}...\nDon't underestimate the guy.");
            await s.Say("jono_sloppy"); await s.Say("jono_there");
            // Seto arrives with his guards: the picture pans up from his feet
            await s.Pan("act1_seto_guards_tall");
            await s.Say("jono_lookat"); await s.Say("jono_groupies"); await s.Say("jono_whip");
            await s.Lit("teana", $"Do it, {Name}!");
            await s.Bg("bg_room"); await s.Chr("chr_seto_room", "seto");
            await s.Say("seto_entertain"); await s.Say("seto_enjoy"); await s.Say("seto_disappoint");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 pre-boss scene: " + ex); }
        await s.Close();
    }

    // ---------------- after the boss's rewards: on to the secret level ----------------
    public static async Task PostBoss(Player player)
    {
        if (await FmScene.Open() is { } s)
        {
            try
            {
                Music(DuelGroundTrack);
                await s.Bg("bg_room"); await s.Chr("chr_seto_room", "seto");
                await s.Say("seto_cannot"); await s.Say("seto_wait"); await s.Say("seto_hmm"); await s.Say("seto_enough");
                await s.Say("seto_paths"); await s.Say("seto_farewell");
                await s.Chr("chr_jonoteana_room", "jono", "teana");
                await s.Lit("jono", $"You did it! {Name}!"); await s.Say("jono_didja");
                await s.Lit("teana", $"{Name}...\nYou're just too good!");
                await s.Lit("jono", "\"I bid you farewell\"...\nHah! What a loser!");
                await s.Hide(1.2);
                // the palace that night
                Music(PalaceTrack);
                await s.Bg("bg_gates", 0.8);
                await s.Wait(1.0);
                await s.Chr("chr_simon_gates", "simon");
                await s.Say("simon_room"); await s.Say("simon_ready");
                await s.Still("act1_simon_study", "simon");
                await s.Say("simon_annoy"); await s.Say("serv_trouble"); await s.Say("serv_mage"); await s.Say("serv_power");
                await s.Say("simon_what"); await s.Say("serv_sorcery"); await s.Say("simon_where"); await s.Say("serv_hall"); await s.Say("simon_dog");
                Music(HeishinTrack);
                await s.Bg("bg_ruin");
                await s.Say("simon_whatthe");
                await s.Still("act1_heishin_ruins", "heishin");
                await s.Say("hei_goodsee"); await s.Say("simon_revolt"); await s.Say("hei_mad"); await s.Say("hei_throne");
                await s.Say("simon_throne"); await s.Say("hei_power"); await s.Say("simon_power"); await s.Say("hei_recog");
                await s.Say("simon_dark"); await s.Say("hei_treasure");
                await s.Still("act1_heishin_rod", "heishin");
                await s.Say("hei_taste");
                await s.Flash();
                await s.Bg("bg_hall");
                await s.Wait(0.6);
                await s.Chr("chr_servant_hall", "servant");
                await s.Lit("servant", $"Prince {Name}!"); await s.Say("isis_invaded"); await s.Say("isis_leave");
                await s.Hide();
                await s.Still("act1_seto_men", "seto", "servant");
                await s.Say("isis_no");
                await s.Lit("seto", $"Prince {Name}...\nDid you hope to escape me?");
                await s.Say("seto_meet"); await s.Say("seto_away"); await s.Say("seto_speak"); await s.Say("guard_cmd");
                Music(SetoNightTrack);
                await s.Bg("bg_pillars"); await s.Chr("chr_seto_pillars", "seto");
                await s.Lit("seto", $"Why do you flee?\n{Name}.");
                await s.Say("seto_captive"); await s.Say("seto_provoke"); await s.Say("seto_want"); await s.Say("seto_iwant");
                await s.Say("seto_location"); await s.Say("seto_spare"); await s.Say("seto_tellme");
                int tell = await Choose(s, 0, "Refuse", "Tell");
                await s.Say(tell == 0 ? "seto_fool" : "seto_talk");
                await s.Say("simon_myprince"); await s.Say("seto_simon");
                await s.Still("act1_simon_puzzle");
                await s.Say("simon_item"); await s.Say("simon_takeit");
                await s.Lit("k", $"{Name} gets Millennium Puzzle!");
                await s.Bg("bg_pillars"); await s.Chr("chr_simon_pillars", "simon");
                await s.Say("simon_keep"); await s.Say("simon_key"); await s.Say("simon_wicked");
                await s.Say("seto_wellwell"); await s.Say("seto_handkey"); await s.Say("simon_cur"); await s.Say("simon_leave");
                await s.Say("seto_outway"); await s.Say("simon_run");
                if (await Choose(s, 0, "Run", "Stay") == 1) await s.Say("simon_listen");
                Music(HeishinTrack);
                await s.Chr("chr_heishin_pillars", "heishin");
                await s.Say("hei_donotmove"); await s.Say("hei_found"); await s.Say("seto_yes"); await s.Say("hei_escape");
                await s.Say("hei_resist"); await s.Say("hei_handover");
                await s.Chr("chr_simon_pillars", "simon");
                await s.Say("simon_option"); await s.Say("simon_battle");
                // FM keeps asking until you agree to the duel
                while (await Choose(s, 0, "Duel", "Pass") == 1)
                {
                    await s.Say("simon_confront"); await s.Say("simon_unleash"); await s.Say("simon_face");
                }
                await s.Chr("chr_heishin_pillars", "heishin");
                await s.Say("hei_wasted");
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 ending scene: " + ex); }
            await s.Close();
        }
        if (Puzzle(player) is { } pz) pz.StoryStage = StageHeishin;
        // the secret level: Heishin's duel, straight away (no map)
        await RunManager.Instance.EnterRoomDebug(RoomType.Boss, MapPointType.Boss, ModelDb.Encounter<FmHeishinEncounter>().ToMutable());
    }

    /// <summary>The secret level begins: Heishin's deck, the Umi field, track 35.</summary>
    public static async Task StartHeishin(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature.CombatState is not { } combat) return;
        State = Phase.HeishinDuel;
        _startRound = combat.RoundNumber;
        Music(HeishinDuelTrack);
        FmRival.Banner("High Mage Heishin challenges you!");
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
        // not a game over: the duelist is back on his feet
        try { await FmRules.Raw(() => CreatureCmd.SetCurrentHp(player.Creature, player.Creature.MaxHp)); } catch (Exception ex) { Log.Warn("[FMDuelist] restore LP: " + ex.Message); }
        if (await FmScene.Open() is { } s)
        {
            try
            {
                await s.Wait(1.5);
                Music(LossTrack);
                await s.Bg("bg_pillars"); await s.Chr("chr_heishin_pillars", "heishin");
                await s.Say("hei_foolish"); await s.Say("hei_rip");
                await s.Still("act1_heishin_simon");
                await s.Say("hei_orelse"); await s.Say("simon_shatter"); await s.Say("simon_vile"); await s.Say("hei_hands");
                await s.Say("simon_puzzle"); await s.Say("simon_dontgive");
                // Simon won't take no for an answer
                while (await Choose(s, 1, "Don't shatter the puzzle", "Shatter the puzzle") == 0)
                    await s.Lit("simon", "But Prince.....\nThe Puzzle");
                await s.Still("act1_puzzle_shatter");
                await s.Say("hei_no");
                await s.Flash();
                Music(PuzzleTrack);
                await s.Bg("bg_inside", 1.0);
                await s.Wait(1.0);
                await s.Chr("chr_simon_inside", "simon");
                await s.Say("simon_awoken"); await s.Say("simon_precise"); await s.Say("simon_sorry"); await s.Say("simon_allow");
                await s.Say("simon_destroy"); await s.Say("simon_oneday"); await s.Say("simon_sleep"); await s.Say("simon_someone");
                await s.Say("simon_guide");
                await s.Lit("simon", "I won't be able to reach you\nwhen you wake up, but please\ntake the remainder of my power.");
                await s.Say("simon_rest");
                await s.Hide(1.0);
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 puzzle scene: " + ex); }
            await s.Close();
        }
        if (Puzzle(player) is { } pz) pz.StoryStage = StagePuzzleDone;
        // the story is over: Heishin leaves, and the reward (Simon's Bright Castle) follows
        TaskHelper.RunSafely(EndCombat(player));
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

    /// <summary>Heishin's room gives only Simon's gift: a Bright Castle card reward (take it or skip it).</summary>
    public static bool HeishinRewards(Player player, List<MegaCrit.Sts2.Core.Rewards.Reward> rewards, AbstractRoom? room)
    {
        if (room is not CombatRoom cr || cr.Encounter is not FmHeishinEncounter) return false;
        rewards.Clear();
        var card = player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(BrightCastle))), player);
        var opts = new CardCreationOptions(new[] { card }, CardCreationSource.Encounter, CardRarityOddsType.Uniform);
        rewards.Add(new MegaCrit.Sts2.Core.Rewards.CardReward(new[] { card }, CardCreationSource.Encounter, player, opts));
        return true;
    }

    /// <summary>The last word before Act 2.</summary>
    public static async Task ComingSoon()
    {
        if (await FmScene.Open() is not { } s) return;
        try { await s.Caption("World Tournament and Final Arc (Act 2 and 3) coming soon, for now Acts 2 and 3 will not include cutscenes but battles can still be played till the end! Good luck!", 2.0); }
        catch (Exception ex) { Log.Warn("[FMDuelist] coming soon: " + ex.Message); }
        await s.Close();
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
