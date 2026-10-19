using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace FMDuelist.Game;

/// <summary>
/// Act 1 as Forbidden Memories' story, on a straight path like the PS1 game: the duelists in the order you meet them
/// (Simon Muran at the palace, Teana and the three villagers at the duel ground, the festival, Jono), with wild
/// monster fights between them, a rest site and a shop, then Young Priest Seto. Each duelist stands in the fight as
/// their FM picture in front of the duel ground (Simon in front of the night palace), with their own deck from the
/// disc, and their pre-duel and after-duel lines from the disc's script.
/// The pictures are local files in the mod's "story" folder (never shipped).
/// </summary>
public static class FmStoryAct1
{
    public sealed record StoryDuel(string Key, string Name, int Index, Type Encounter, Type Monster, string Bg, string Chr);

    public static readonly StoryDuel[] Duels =
    {
        new("simon", "Simon Muran", 1, typeof(FmEncSimon), typeof(FmDuelSimon), "bg_palace", "chr_simon_palace"),
        new("teana", "Teana", 2, typeof(FmEncTeana), typeof(FmDuelTeana), "bg_room", "chr_teana_room"),
        new("v1", "Villager 1", 4, typeof(FmEncVillager1), typeof(FmDuelVillager1), "bg_room", "chr_v1_room"),
        new("v2", "Villager 2", 5, typeof(FmEncVillager2), typeof(FmDuelVillager2), "bg_room", "chr_v2_room"),
        new("v3", "Villager 3", 6, typeof(FmEncVillager3), typeof(FmDuelVillager3), "bg_room", "chr_v3_room"),
        new("jono", "Jono", 3, typeof(FmEncJono), typeof(FmDuelJono), "bg_room", "chr_jono_cols"),
    };

    public static IEnumerable<FmRival.Duelist> Duelists => Duels.Select(d => new FmRival.Duelist(d.Encounter, d.Monster, d.Name, d.Index, 8000, 0));
    public static StoryDuel? ForMonster(Type? t) => Duels.FirstOrDefault(d => d.Monster == t);
    public static StoryDuel? ForKey(string? k) => Duels.FirstOrDefault(d => d.Key == k);
    public static StoryDuel? ForName(string? n) => Duels.FirstOrDefault(d => d.Name == n);

    /// <summary>The path, row by row after the starting Ancient (Simon's scolding); the boss (Seto) follows.</summary>
    public static readonly (MapPointType Type, string? Story)[] Path =
    {
        // every two duels, a wild monster (null)
        (MapPointType.Monster, "simon"),
        (MapPointType.Monster, "teana"),
        (MapPointType.Monster, null),
        (MapPointType.Shop, null),
        (MapPointType.RestSite, null),
        (MapPointType.Monster, "v1"),
        (MapPointType.Monster, "v2"),
        (MapPointType.Monster, null),
        (MapPointType.Shop, null),
        (MapPointType.Monster, "v3"),
        (MapPointType.Unknown, "festival"),
        (MapPointType.Monster, "jono"),
        (MapPointType.Monster, null),
        (MapPointType.RestSite, null),
    };

    /// <summary>What the map point on this act floor is in the story (floor 1 is the starting Ancient).</summary>
    public static string? StoryAt(int actFloor) => actFloor - 2 is var i && i >= 0 && i < Path.Length ? Path[i].Story : null;

    public static bool StoryRun(IRunState? s) => s != null && s.CurrentActIndex == 0 && s.Players.FirstOrDefault() is { } p && FmRules.IsFmPlayer(p)
        && p.GetRelic<Relics.MillenniumPuzzle>() is { StoryMap: true };

    // ---------------- the duelist on the field ----------------
    static Control? _bgRoot;

    /// <summary>The duelist stands as their picture (the stand-in body hidden) in front of their place.</summary>
    /// <summary>Half the width of the dressed duelist's picture (his zones keep clear of it).</summary>
    public static float HalfWidth;

    /// <summary>A story duel is on (its layout is the compact one, like the Free Duel).</summary>
    public static bool InDuel => FmRival.Active && (ForMonster(FmRival.Current?.Monster) != null || _dressed);
    static bool _dressed;

    /// <summary>The Act 1 boss as FM's Priest Seto, in front of the night pillars of his scene.</summary>
    public static readonly StoryDuel SetoLook = new("seto", "Young Priest Seto", 7, typeof(MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss), typeof(MegaCrit.Sts2.Core.Models.Monsters.KinPriest), "bg_cols", "chr_seto_cols");
    /// <summary>The secret level's Heishin (his duel keeps the Umi field as its scenery).</summary>
    public static readonly StoryDuel HeishinLook = new("heishin", "High Mage Heishin", 8, typeof(FmHeishinEncounter), typeof(FmHeishin), "", "chr_heishin_pillars");

    public static void Dress(Creature duelist, StoryDuel d)
    {
        try
        {
            var room = NCombatRoom.Instance;
            if (room?.GetCreatureNode(duelist) is not { } node) return;
            if (node.Visuals.GetCurrentBody() is { } body) body.Visible = false;
            if (FmScene.Tex("duelist_" + d.Key) is { } tex)
            {
                const int h = 250; // small enough that duelist and zones fit on screen
                var small = FmScene.Sized(tex, h);
                var spr = new Sprite2D { Texture = small, Position = new Vector2(0, -h / 2f), Centered = true, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
                HalfWidth = small.GetWidth() * 0.5f;
                node.Visuals.AddChild(spr);
                spr.Modulate = new Color(1, 1, 1, 0);
                var t = spr.CreateTween(); t.TweenProperty(spr, "modulate:a", 1f, 0.5);
            }
            _dressed = true;
            // the fight keeps the game's own background (no FM places behind duels)
            // you at the left and the duelist at the right, each with three evenly spaced zones in front
            if (FmRival.Current is { } cur && FmPractice.Duelist != cur && duelist.CombatState?.Players.FirstOrDefault() is { } p)
            {
                FmDuelLayout.Duel(p);
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] dress duelist: " + ex.Message); }
    }

    /// <summary>The combat background becomes one of FM's places (the duel ground, the palace at night).</summary>
    public static void Background(string key)
    {
        var room = NCombatRoom.Instance;
        if (room?.Background?.GetParent() is not Control container || FmScene.Tex(key) is not { } tex) return;
        if (_bgRoot != null && GodotObject.IsInstanceValid(_bgRoot)) _bgRoot.QueueFree();
        var rect = new TextureRect
        {
            Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(0.85f, 0.85f, 0.85f, 0),
        };
        // the container has no size of its own: cover the same area as the game's background (or the screen)
        var size = room.Background.Size;
        if (size.X < 10 || size.Y < 10) size = room.GetViewportRect().Size;
        rect.Position = room.Background.Position;
        rect.Size = size;
        container.AddChild(rect);
        container.MoveChild(rect, room.Background.GetIndex() + 1);
        _bgRoot = rect;
        var t = rect.CreateTween(); t.TweenProperty(rect, "modulate:a", 1f, 0.6);
    }

    public static void ClearBackground()
    {
        _dressed = false;
        if (_bgRoot != null && GodotObject.IsInstanceValid(_bgRoot)) _bgRoot.QueueFree();
        _bgRoot = null;
    }
}

// ---------------- the straight path ----------------
/// <summary>Act 1's map: one straight line, like the game's Golden Path.</summary>
public sealed class FmStoryActMap : ActMap
{
    public override MapPoint BossMapPoint { get; }
    public override MapPoint StartingMapPoint { get; }
    protected override MapPoint?[,] Grid { get; }

    public FmStoryActMap()
    {
        var path = FmStoryAct1.Path;
        Grid = new MapPoint[7, path.Length + 1];
        BossMapPoint = new MapPoint(GetColumnCount() / 2, GetRowCount()) { PointType = MapPointType.Boss };
        StartingMapPoint = new MapPoint(GetColumnCount() / 2, 0) { PointType = MapPointType.Ancient };
        for (int i = 0; i < path.Length; i++)
        {
            var p = new MapPoint(3, i + 1) { PointType = path[i].Type };
            Grid[3, i + 1] = p;
            if (i > 0) Grid[3, i]!.AddChildPoint(p);
        }
        startMapPoints.Add(Grid[3, 1]!);
        Grid[3, GetRowCount() - 1]!.AddChildPoint(BossMapPoint);
        StartingMapPoint.AddChildPoint(Grid[3, 1]!);
    }
}

[HarmonyPatch]
static class FmStoryRoomsPatch
{
    /// <summary>The story's points get their duelist (or the festival) instead of a random room.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(RunManager), "CreateRoom")]
    static bool Room(RoomType roomType, AbstractModel? model, ref AbstractRoom __result)
    {
        try
        {
            if (model != null) return true;
            var s = RunManager.Instance.DebugOnlyGetState();
            if (!FmStoryAct1.StoryRun(s)) return true;
            var story = FmStoryAct1.StoryAt(s!.ActFloor);
            if (story == "festival" && roomType == RoomType.Event)
            {
                __result = new EventRoom(ModelDb.Event<FmFestivalEvent>());
                return false;
            }
            if (FmStoryAct1.ForKey(story) is { } d && roomType is RoomType.Monster or RoomType.Elite)
            {
                var enc = (EncounterModel)AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Encounter)).MakeGenericMethod(d.Encounter).Invoke(null, null)!;
                __result = new CombatRoom(enc.ToMutable(), (RunState)s);
                return false;
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] story room: " + ex.Message); }
        return true;
    }

    /// <summary>
    /// Leaving Act 1 after Seto's rewards: the story's ending scenes and Heishin's secret level come first; after
    /// Simon's gift, "coming soon", then Act 2 as usual.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterNextAct))]
    static bool NextAct(ref Task __result)
    {
        try
        {
            var s = RunManager.Instance.DebugOnlyGetState();
            if (!FmStoryAct1.StoryRun(s) || s!.Players.First().GetRelic<Relics.MillenniumPuzzle>() is not { } pz) return true;
            var player = s.Players.First();
            if (pz.StoryStage == FmAct1Story.StageNone)
            {
                __result = FmAct1Story.PostBoss(player);
                return false;
            }
            if (pz.StoryStage == FmAct1Story.StagePuzzleDone)
            {
                pz.StoryStage = FmAct1Story.StageDone;
                __result = Then(FmAct1Story.ComingSoon(), () => RunManager.Instance.EnterAct(1));
                return false;
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 ending: " + ex.Message); }
        return true;
    }

    static async Task Then(Task first, Func<Task> next) { await first; await next(); }

    /// <summary>Duel points on the map show the duelist's portrait.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(NNormalMapPoint), "UpdateIcon")]
    static void Icon(NNormalMapPoint __instance)
    {
        try
        {
            var s = RunManager.Instance.DebugOnlyGetState();
            if (!FmStoryAct1.StoryRun(s)) return;
            int row = __instance.Point.coord.row;
            if (row < 1 || row > FmStoryAct1.Path.Length || FmStoryAct1.ForKey(FmStoryAct1.Path[row - 1].Story) is not { } d) return;
            if (FmScene.Tex("map_" + d.Key) is not { } tex) return;
            var t = Traverse.Create(__instance);
            if (t.Field("_icon").GetValue<TextureRect>() is { } icon)
            {
                icon.Texture = tex;
                icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            }
            if (t.Field("_outline").GetValue<TextureRect>() is { } outline) outline.Visible = false;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] map portrait: " + ex.Message); }
    }
}

// ---------------- the festival: a "?" point that is just the cutscene ----------------
public sealed class FmFestivalEvent : EventModel
{
    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
        new[] { new EventOption(this, Gift, Id.Entry + ".pages.INITIAL.options.CONTINUE") };

    /// <summary>Leaving the festival: one of the mages' gifts, a Millennium Item.</summary>
    async Task Gift()
    {
        try
        {
            if (Owner != null && Relics.FmRelicDrops.Random(Owner, festival: true) is { } relic)
                await MegaCrit.Sts2.Core.Commands.RelicCmd.Obtain(relic, Owner);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] festival gift: " + ex.Message); }
        SetEventFinished(L10NLookup(Id.Entry + ".pages.DONE.description"));
    }

    protected override async Task BeforeEventStarted(bool isPreFinished)
    {
        if (isPreFinished) return;
        try { await FmAct1Story.Festival(); } catch (Exception ex) { Log.Warn("[FMDuelist] festival: " + ex); }
    }


    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState).Where(p => !p.Contains(Id.Entry.ToLowerInvariant()));
}

[HarmonyPatch(typeof(EventModel), nameof(EventModel.CreateInitialPortrait))]
static class FmFestivalPortraitPatch
{
    static bool Prefix(EventModel __instance, ref Texture2D __result)
    {
        if (__instance is not FmFestivalEvent) return true;
        __result = FmScene.Tex("act1_procession") ?? ImageTexture.CreateFromImage(Image.CreateEmpty(8, 8, false, Image.Format.Rgba8));
        return false;
    }
}

// ---------------- the duelists ----------------
public abstract class FmStoryDuelistMonster : MonsterModel
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
        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState("Dead", new AnimState("die"));
        animator.AddAnyState("Hit", hurt);
        return animator;
    }
}
public sealed class FmDuelSimon : FmStoryDuelistMonster { }
public sealed class FmDuelTeana : FmStoryDuelistMonster { }
public sealed class FmDuelVillager1 : FmStoryDuelistMonster { }
public sealed class FmDuelVillager2 : FmStoryDuelistMonster { }
public sealed class FmDuelVillager3 : FmStoryDuelistMonster { }
public sealed class FmDuelJono : FmStoryDuelistMonster { }

public abstract class FmStoryEncounter<T> : EncounterModel where T : MonsterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[] { ModelDb.Monster<T>() };
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() => new (MonsterModel, string?)[] { (ModelDb.Monster<T>().ToMutable(), null) };
}
public sealed class FmEncSimon : FmStoryEncounter<FmDuelSimon> { }
public sealed class FmEncTeana : FmStoryEncounter<FmDuelTeana> { }
public sealed class FmEncVillager1 : FmStoryEncounter<FmDuelVillager1> { }
public sealed class FmEncVillager2 : FmStoryEncounter<FmDuelVillager2> { }
public sealed class FmEncVillager3 : FmStoryEncounter<FmDuelVillager3> { }
public sealed class FmEncJono : FmStoryEncounter<FmDuelJono> { }

/// <summary>The secret level after the Act 1 boss: High Mage Heishin's unwinnable duel.</summary>
public sealed class FmHeishinEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Boss;
    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[] { ModelDb.Monster<FmHeishin>() };
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() => new (MonsterModel, string?)[] { (ModelDb.Monster<FmHeishin>().ToMutable(), null) };
}
