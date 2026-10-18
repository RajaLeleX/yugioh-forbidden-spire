using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using FMDuelist.Cards;
using FMDuelist.Data;

namespace FMDuelist.Game;

/// <summary>
/// "Duel the Duel Master K" (in place of the Compendium): a free practice duel from the main menu against a mirror
/// Duel Master K. You pick level 1, 2 or 3 (Act 1, 2 or 3 deck strength) and both duelists use copies of the same
/// premade 40-card deck. Win or lose, you're back at the main menu: no rewards, and nothing is saved.
/// </summary>
public static class FmPractice
{
    public static bool Active;
    public static int Level = 1;
    /// <summary>The Free Duel's music (looped).</summary>
    public const int MusicTrack = 39;

    /// <summary>The three premade decks, built once in the background when the disc loads.</summary>
    static readonly List<int>?[] _decks = new List<int>?[3];

    public static void Preload()
    {
        Task.Run(() =>
        {
            try { for (int l = 1; l <= 3; l++) _decks[l - 1] = Build(l); Log.Info($"[FMDuelist] Free Duel decks ready: {string.Join(", ", _decks.Select(d => d?.Count ?? 0))} cards"); }
            catch (Exception ex) { Log.Warn("[FMDuelist] Free Duel decks: " + ex.Message); }
        });
    }

    public static readonly FmRival.Duelist Duelist = new(typeof(FmPracticeEncounter), typeof(FmMirrorDK), "Duel Master K", -1, 8000, 0);

    // ---------------- the field: one half each ----------------
    /// <summary>Zone spacing in the Free Duel (tighter than a boss fight's, so both rows fit).</summary>
    public const float ZoneGap = 105f, ZoneStep = 165f;
    /// <summary>The Free Duel and the story duels use the compact layout (both duelists at the screen's edges).</summary>
    public static bool Compact => Active || FmStoryAct1.InDuel;
    static CanvasLayer? _labels;

    /// <summary>You stand at the far left and Duel Master K at the far right, each with your own three zones in front.</summary>
    public static void Layout(MegaCrit.Sts2.Core.Entities.Players.Player player, Creature rival)
    {
        try
        {
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (!Place(player, rival) || room?.GetCreatureNode(player.Creature) is not { } me || room.GetCreatureNode(rival) is not { } him) return;
            ShowLabels(me, him);
            // not a real run: no top bar (gold, floor, potions, map, deck) and no relic row
            if (MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GlobalUi is { } ui)
            {
                ui.TopBar.Visible = false;
                ui.RelicInventory.Visible = false;
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] free duel layout: " + ex.Message); }
    }

    /// <summary>You at 12% of the screen and your rival at 88%; Osty (your first zone) follows you.</summary>
    public static bool Place(MegaCrit.Sts2.Core.Entities.Players.Player player, Creature rival)
    {
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (room?.GetCreatureNode(player.Creature) is not { } me || room.GetCreatureNode(rival) is not { } him) return false;
        var view = room.GetViewportRect().Size;
        var toWorld = room.GetViewport().GetCanvasTransform().AffineInverse();
        float y = me.GlobalPosition.Y;
        me.GlobalPosition = new Vector2((toWorld * new Vector2(view.X * 0.12f, 0)).X - me.Size.X * 0.5f, y);
        him.GlobalPosition = new Vector2((toWorld * new Vector2(view.X * 0.88f, 0)).X - him.Size.X * 0.5f, y);
        foreach (var pet in player.Creature.Pets)
            if (room.GetCreatureNode(pet) is { } pn && pet.Monster is not FmZonePet) pn.GlobalPosition = me.GlobalPosition + new Vector2(me.Hitbox.Size.X * 0.5f + ZoneGap * 0.4f, 0);
        return true;
    }

    /// <summary>FM's YOU / COM tags over each Duel Master K.</summary>
    static void ShowLabels(MegaCrit.Sts2.Core.Nodes.Combat.NCreature me, MegaCrit.Sts2.Core.Nodes.Combat.NCreature him)
    {
        RemoveLabels();
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _labels = new CanvasLayer { Layer = 6 };
        tree.Root.AddChild(_labels);
        void Tag(MegaCrit.Sts2.Core.Nodes.Combat.NCreature who, string tag, string name, Color color)
        {
            var r = who.Hitbox.GetGlobalRect();
            var screen = who.GetViewport().GetCanvasTransform() * new Vector2(r.GetCenter().X, r.Position.Y);
            var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = color, BorderColor = Colors.White, BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 2, ContentMarginBottom = 2 });
            var l = new Label { Text = $"{tag}  {name}", MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 26);
            l.AddThemeColorOverride("font_color", Colors.White);
            l.AddThemeConstantOverride("outline_size", 6);
            l.AddThemeColorOverride("font_outline_color", Colors.Black);
            box.AddChild(l);
            _labels!.AddChild(box);
            // at the top of the screen, in the duelist's corner (clear of the zones, traps and field box)
            var view = who.GetViewport().GetVisibleRect().Size;
            bool right = tag == "COM";
            box.Position = new Vector2(right ? view.X - 24 : 24, 18);
            if (right) box.Resized += () => box.Position = new Vector2(view.X - 24 - box.Size.X, 18);
            _ = screen;
        }
        Tag(me, "YOU", "Duel Master K", new Color(0.75f, 0.05f, 0.05f, 0.9f));
        Tag(him, "COM", "Duel Master K", new Color(0.1f, 0.2f, 0.75f, 0.9f));
    }

    public static void RemoveLabels()
    {
        if (_labels != null && GodotObject.IsInstanceValid(_labels)) _labels.QueueFree();
        _labels = null;
    }

    // ---------------- the premade decks ----------------
    static readonly int[][] Spells =
    {
        new[] { 338, 338, 343, 343, 344, 302, 308, 311, 681, 682 },             // Act 1: small heals and burns, basic equips, weak traps
        new[] { 339, 339, 344, 345, 346, 301, 305, 323, 669, 683 },             // Act 2: stronger heals and burns, Shadow Spell
        new[] { 340, 341, 345, 346, 347, 337, 336, 657, 684, 686 },             // Act 3: big heals, Raigeki, Dark Hole, Megamorph
    };
    static readonly (int min, int max)[] AtkBand = { (800, 1400), (1400, 2000), (1900, 2900) };

    /// <summary>The 40-card deck for a level: 30 monsters spread across the level's ATK band, plus its spells and traps.</summary>
    public static List<int> Deck(int level) => level == YourDeckLevel && _yours != null ? new List<int>(_yours)
        : _decks[Math.Clamp(level, 1, 3) - 1] is { } d ? new List<int>(d) : Build(level);

    // ---------------- your own deck ----------------
    public const int YourDeckLevel = 4;
    static List<int>? _yours;

    /// <summary>
    /// Your deck: the one in your saved run if there is one, otherwise the deck you ended your last run with
    /// (newest run history first). Only Forbidden Memories cards count.
    /// </summary>
    static List<int>? YourDeck(out string from)
    {
        from = "";
        static List<int> Ids(IEnumerable<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard> cards) => cards
            .Select(c => c.Id is { } id ? ModelDb.GetById<CardModel>(id) as Cards.FmCardBase : null)
            .Where(c => c != null && c.FmId > 0 && FmData.Get(c.FmId) is { Name.Length: > 0 })
            .Select(c => c!.FmId).ToList();
        try
        {
            var sm = SaveManager.Instance;
            if (sm.HasRunSave && sm.LoadRunSave().SaveData is { } run && run.Players?.FirstOrDefault() is { } me)
            {
                var ids = Ids(me.Deck);
                if (ids.Count >= 5) { from = "current run"; return ids; }
            }
            foreach (var name in sm.GetAllRunHistoryNames().OrderByDescending(n => n))
            {
                if (sm.LoadRunHistory(name).SaveData is not { } h || h.GameMode == GameMode.Custom) continue;
                if (h.Players.FirstOrDefault() is not { } p) continue;
                var ids = Ids(p.Deck);
                if (ids.Count >= 5) { from = "last run"; return ids; }
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] your deck: " + ex.Message); }
        return null;
    }

    static List<int> Build(int level)
    {
        int l = Math.Clamp(level, 1, 3) - 1;
        var (min, max) = AtkBand[l];
        var pool = Enumerable.Range(1, 722)
            .Where(id => FmData.Get(id) is { IsMonster: true } c && c.Atk >= min && c.Atk <= max && (l == 2 || c.Level < FmRules.StrongDragonLevel || c.Type != FmType.Dragon))
            .Where(id => FmSheet.PoolTypes.Contains(FmSheet.TypeFor(id)))
            .OrderBy(id => FmData.Get(id).Atk).ThenBy(id => id).ToList();
        var deck = new List<int>();
        if (pool.Count > 0)
            for (int i = 0; i < 30; i++) deck.Add(pool[(int)((i + 0.5) * pool.Count / 30.0) % pool.Count]);
        deck.AddRange(Spells[l]);
        return deck;
    }

    // ---------------- starting it ----------------
    public static async Task Start(int level)
    {
        try
        {
            Active = true; Level = level;
            if (level == YourDeckLevel && _yours == null) _yours = YourDeck(out _);
            Log.Info($"[FMDuelist] Practice duel vs Duel Master K, level {level}");
            var seed = DateTime.Now.Ticks.ToString("X")[^8..];
            await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Necrobinder>(), false, ActModel.GetDefaultList(), Array.Empty<ModifierModel>(), seed, GameMode.Custom);
            var player = RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault();
            if (player != null)
            {
                // both duelists use copies of the level's deck
                var old = player.Deck.Cards.ToList();
                if (old.Count > 0) await CardPileCmd.RemoveFromDeck(old, false);
                foreach (var id in Deck(level))
                    await CardPileCmd.Add(player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player), PileType.Deck, skipVisuals: true);
            }
            var encounter = ModelDb.Encounter<FmPracticeEncounter>().ToMutable();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, encounter);
        }
        catch (Exception ex) { Log.Error("[FMDuelist] practice start: " + ex); Active = false; }
    }

    /// <summary>The practice duel is over: results, then straight back to the main menu.</summary>
    public static async Task Finish(bool won, MegaCrit.Sts2.Core.Entities.Players.Player player, int comLp, int turns)
    {
        try
        {
            var loser = won ? FmRival.Priest : player.Creature;
            if (loser != null) await CreatureCmd.TriggerAnim(loser, "Dead", 1.4f);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] practice death anim: " + ex.Message); }
        try { await FmDuelEnd.Show(won, player, comLp, "Duel Master K", turns, spoils: false); }
        catch (Exception ex) { Log.Warn("[FMDuelist] practice results: " + ex.Message); }
        FmPracticeNoSavePatch.Guard = true;
        Active = false;
        FmRival.Reset();
        try { await NGame.Instance!.ReturnToMainMenu(); } catch (Exception ex) { Log.Error("[FMDuelist] practice exit: " + ex); }
        finally { FmPracticeNoSavePatch.Guard = false; }
    }

    // ---------------- the level picker ----------------
    static CanvasLayer? _picker;

    public static void ShowPicker()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_picker != null && GodotObject.IsInstanceValid(_picker)) return;
        FmMusic.Stop(); // silence until a level is picked
        _picker = new CanvasLayer { Layer = 90 };
        tree.Root.AddChild(_picker);
        var view = _picker.GetViewport().GetVisibleRect().Size;
        float s = view.Y / 1080f;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.75f), Size = view, MouseFilter = Control.MouseFilterEnum.Stop };
        _picker.AddChild(dim);
        var panel = new PanelContainer { Size = new Vector2(760 * s, 690 * s), Position = new Vector2((view.X - 760 * s) / 2, (view.Y - 690 * s) / 2) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.06f, 0.12f, 0.97f), BorderColor = new Color(0.86f, 0.68f, 0.28f),
            BorderWidthBottom = 4, BorderWidthTop = 4, BorderWidthLeft = 4, BorderWidthRight = 4,
            CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14,
            ContentMarginLeft = 40 * s, ContentMarginRight = 40 * s, ContentMarginTop = 30 * s, ContentMarginBottom = 30 * s,
        });
        _picker.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(18 * s));
        panel.AddChild(col);
        Label L(string t, int size, Color c) { var l = new Label { Text = t, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart }; l.AddThemeFontSizeOverride("font_size", (int)(size * s)); l.AddThemeColorOverride("font_color", c); return l; }
        col.AddChild(L("Duel the Duel Master K", 44, new Color(1f, 0.85f, 0.35f)));
        col.AddChild(L("A free practice duel. You and Duel Master K both use copies of the same premade deck. No rewards: you return to the main menu when the duel ends.", 24, new Color(0.85f, 0.85f, 0.9f)));
        string[] names = { "Level 1 - The Dueling Grounds (Act 1 deck)", "Level 2 - The World Tournament (Act 2 deck)", "Level 3 - Seto's Kingdom (Act 3 deck)" };
        for (int i = 0; i < 3; i++)
        {
            int level = i + 1;
            var b = new Button { Text = names[i], CustomMinimumSize = new Vector2(0, 74 * s) };
            b.AddThemeFontSizeOverride("font_size", (int)(28 * s));
            b.Pressed += () => { ClosePicker(); TaskHelper.RunSafely(Start(level)); };
            col.AddChild(b);
        }
        // 4th: your own deck (from your saved run, or your last run); Duel Master K uses a copy of it too
        _yours = YourDeck(out var from);
        var mine = new Button
        {
            Text = _yours != null ? $"Level 4 - Your Deck ({from}, {_yours.Count} cards)" : "Level 4 - Your Deck (no run found yet)",
            CustomMinimumSize = new Vector2(0, 74 * s), Disabled = _yours == null,
        };
        mine.AddThemeFontSizeOverride("font_size", (int)(28 * s));
        mine.Pressed += () => { ClosePicker(); TaskHelper.RunSafely(Start(YourDeckLevel)); };
        col.AddChild(mine);
        var cancel = new Button { Text = "Back", CustomMinimumSize = new Vector2(0, 60 * s) };
        cancel.AddThemeFontSizeOverride("font_size", (int)(26 * s));
        cancel.Pressed += () => { ClosePicker(); FmMusic.PlayWhenReady(FmMedia.MenuTrack); };
        col.AddChild(cancel);
    }

    static void ClosePicker()
    {
        if (_picker != null && GodotObject.IsInstanceValid(_picker)) _picker.QueueFree();
        _picker = null;
    }
}

/// <summary>
/// The Free Duel is a throwaway run: while it's on (and until it's back at the main menu), nothing is saved and
/// no save is deleted, so the run you have saved, its history and your progress are left exactly as they were.
/// </summary>
[HarmonyPatch]
static class FmPracticeNoSavePatch
{
    public static bool Guard;

    [HarmonyPrefix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
    static bool NoSave(ref Task __result)
    {
        if (!FmPractice.Active && !Guard) return true;
        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
    static bool NoDelete() => !FmPractice.Active && !Guard;

    [HarmonyPrefix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRunHistory))]
    static bool NoHistory() => !FmPractice.Active && !Guard;

    [HarmonyPrefix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.UpdateProgressWithRunData))]
    static bool NoProgress() => !FmPractice.Active && !Guard;
}

/// <summary>
/// Giving up in the Free Duel counts as losing it: your Duel Master K falls, "YOU LOSE" and the results play,
/// then it's back to the main menu (instead of the run just fading out).
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.Abandon))]
static class FmPracticeGiveUpPatch
{
    static bool Prefix()
    {
        if (!FmPractice.Active) return true;
        var state = RunManager.Instance.DebugOnlyGetState();
        var player = state?.Players.FirstOrDefault();
        if (player == null) return true;
        try { MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance?.Close(); } catch { }
        int turns = player.Creature.CombatState?.RoundNumber ?? 1;
        int comLp = Math.Max(0, FmRival.Priest?.CurrentHp ?? 0);
        FmRival.Active = false;
        TaskHelper.RunSafely(FmPractice.Finish(false, player, comLp, turns));
        return false;
    }
}

/// <summary>
/// Leaving a run by any route (give up, death, quitting to the menu) clears every duel overlay the mod puts on
/// screen: his hand and set traps, the YOU/COM tags, the commentary line, your field row.
/// </summary>
[HarmonyPatch]
static class FmLeaveRunCleanupPatch
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (var n in new[] { nameof(NGame.ReturnToMainMenu), nameof(NGame.ReturnToMainMenuAfterRun), nameof(NGame.GoToTimelineAfterRun) })
            if (AccessTools.Method(typeof(NGame), n) is { } m) yield return m;
    }

    static void Prefix()
    {
        try
        {
            FmRival.Reset();
            FmRival.ClearBanner();
            FmPractice.RemoveLabels();
            FmField.ResetCombat();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] leave-run cleanup: " + ex.Message); }
    }
}

/// <summary>The Compendium button becomes "Duel the Duel Master K".</summary>
[HarmonyPatch]
static class FmPracticeMenuPatch
{
    [HarmonyPrefix, HarmonyPatch(typeof(NMainMenu), "OpenCompendiumSubmenu", new[] { typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NButton) })]
    static bool Open()
    {
        FmPractice.ShowPicker();
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.IsCompendiumAvailable))]
    static void Always(ref bool __result) => __result = true;
}

/// <summary>The practice duel's only enemy: Duel Master K himself.</summary>
public sealed class FmPracticeEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[] { ModelDb.Monster<FmMirrorDK>() };
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() => new (MonsterModel, string?)[] { (ModelDb.Monster<FmMirrorDK>().ToMutable(), null) };
}

/// <summary>A mirror Duel Master K: the Necrobinder's own skeleton, turned to face you.</summary>
public sealed class FmMirrorDK : MonsterModel
{
    public override int MinInitialHp => 80;
    public override int MaxInitialHp => 80;
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/necrobinder");

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
        var cast = new AnimState("cast"); cast.NextState = idle;
        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState("Dead", new AnimState("die"));
        animator.AddAnyState("Hit", hurt);
        animator.AddAnyState("Cast", cast);
        return animator;
    }
}
