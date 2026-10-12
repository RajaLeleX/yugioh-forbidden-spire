import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:90]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Game/FmRival.cs', [
('''    public static IEnumerable<Duelist> AllDuelists => Bosses.Append(FmPractice.Duelist).Append(FmAct1Story.Heishin);''',
 '''    public static IEnumerable<Duelist> AllDuelists => Bosses.Append(FmPractice.Duelist).Append(FmAct1Story.Heishin).Concat(FmStoryAct1.Duelists);'''),
])

R = 'Relics/MillenniumPuzzle.cs'
patch(R, [
('''    private readonly HashSet<Creature> _scaled = new();''', '''    private readonly HashSet<Creature> _scaled = new();
    private int _storyStage;

    /// <summary>How far the Act 1 story ending has got (see FmAct1Story.Stage*), saved with the run.</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int StoryStage { get => _storyStage; set { AssertMutable(); _storyStage = value; } }

    /// <summary>Act 1 is Forbidden Memories' story on one straight path.</summary>
    public override MegaCrit.Sts2.Core.Map.ActMap ModifyGeneratedMap(MegaCrit.Sts2.Core.Runs.IRunState runState, MegaCrit.Sts2.Core.Map.ActMap map, int actIndex)
        => actIndex == 0 && runState.Players.Contains(Owner) ? new FmStoryActMap() : map;

    public override bool TryModifyRewards(Player player, List<MegaCrit.Sts2.Core.Rewards.Reward> rewards, AbstractRoom? room)
        => Mine(player) && FmAct1Story.HeishinRewards(player, rewards, room);'''),
('''        Track(); FmInput.Ensure(); FmMusic.OnRoomEntered(room, Owner);''', '''        Track(); FmInput.Ensure(); FmMusic.OnRoomEntered(room, Owner);
        // the start of the story: Simon scolds the prince (before the starting choice)
        if (room is EventRoom er && er.CanonicalEvent is MegaCrit.Sts2.Core.Models.Events.Neow && Owner.RunState.CurrentActIndex == 0 && !FmPractice.Active)
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(FmAct1Story.SimonScolds());'''),
('''        // boss duel: the Kin Priest duels by FM rules
        if (Owner.Creature.CombatState!.Enemies.Any(e => e.Monster != null && FmRival.AllDuelists.Any(b => b.Monster == e.Monster.GetType()))) await FmRival.Begin(Owner, Owner.Creature.CombatState!);''',
 '''        // Act 1 story duels: the duelist's lines first
        var storyDuel = Owner.Creature.CombatState!.Enemies.Select(e => FmStoryAct1.ForMonster(e.Monster?.GetType())).FirstOrDefault(d => d != null);
        if (storyDuel != null && !FmPractice.Active)
        {
            try { await FmAct1Story.PreDuel(storyDuel); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] pre-duel: " + ex.Message); }
            FmMusic.OnRoomEntered(Owner.RunState.CurrentRoom!, Owner);
        }
        // boss duel: the Kin Priest duels by FM rules
        if (Owner.Creature.CombatState!.Enemies.Any(e => e.Monster != null && FmRival.AllDuelists.Any(b => b.Monster == e.Monster.GetType()))) await FmRival.Begin(Owner, Owner.Creature.CombatState!);
        if (storyDuel != null && FmRival.Priest != null) FmStoryAct1.Dress(FmRival.Priest, storyDuel);
        // the secret level: Heishin's unwinnable duel
        if (Owner.Creature.CombatState!.Enemies.Any(FmAct1Story.IsHeishin))
            await FmAct1Story.StartHeishin(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), Owner);'''),
('''        // Act 1 story: beating Seto leads into Heishin's invasion; losing to Heishin leads into the Puzzle
        if (FmRival.Active && !FmPractice.Active && FmRival.Current == FmRival.Bosses[0] && creature == FmRival.Priest && FmAct1Story.Enabled(Owner))
        {
            await FmAct1Story.AfterSeto(Owner, Owner.Creature.CombatState?.RoundNumber ?? 1);
            return;
        }
        if (FmAct1Story.State''', '''        // Act 1 story: losing to Heishin leads into the Puzzle
        if (FmAct1Story.State'''),
('''        await FmDuelEnd.Show(won, Owner, comLp, FmRival.Current.Name, turns);
    }''', '''        var duel = FmRival.Current;
        await FmDuelEnd.Show(won, Owner, comLp, duel.Name, turns);
        // a story duelist's closing lines
        if (won && FmStoryAct1.ForMonster(duel.Monster) is { } sd) await FmAct1Story.PostDuel(sd);
    }'''),
('''        if (FmAct1Story.State == FmAct1Story.Phase.HeishinPending && creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.KinPriest)
            await FmAct1Story.StartHeishin(ctx, Owner);
''', ''),
('''        FmRival.Reset();
        FmAct1Story.Reset();
        return Task.CompletedTask;''', '''        FmRival.Reset();
        FmAct1Story.Reset();
        FmStoryAct1.ClearBackground();
        return Task.CompletedTask;'''),
])

# leaving Act 1: the post-boss story and the secret level, then "coming soon"
patch('Game/FmStoryAct1.cs', [
('''    /// <summary>Duel points on the map show the duelist's portrait.</summary>''', '''    /// <summary>
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

    /// <summary>Duel points on the map show the duelist's portrait.</summary>'''),
])

# Simon's scolding waits for the intro
patch('Game/FmStoryIntro.cs', [
('''    static bool _skip;''', '''    static bool _skip;
    /// <summary>The intro is about to play or playing (story scenes wait for it).</summary>
    public static bool Playing;'''),
('''    static void Postfix(CharacterModel character, ref Task<RunState> __result)
    {
        if (character is not Necrobinder || FmPractice.Active) return;
        __result = After(__result);
    }''', '''    static void Prefix(CharacterModel character)
    {
        if (character is Necrobinder && !FmPractice.Active) FmStoryIntro.Playing = true;
    }

    static void Postfix(CharacterModel character, ref Task<RunState> __result)
    {
        if (character is not Necrobinder || FmPractice.Active) return;
        __result = After(__result);
    }'''),
('''        var state = await start;
        if (!FmPractice.Active) await FmStoryIntro.Play();
        return state;''', '''        try
        {
            var state = await start;
            if (!FmPractice.Active) await FmStoryIntro.Play();
            return state;
        }
        finally { FmStoryIntro.Playing = false; }'''),
])

# who speaks
patch('Game/FmScene.cs', [
('''        ["guard"] = "Guard", ["servant"] = "Servant", ["k"] = "K",''', '''        ["guard"] = "Guard", ["servant"] = "Servant", ["k"] = "K", ["v1"] = "Villager 1", ["v2"] = "Villager 2", ["v3"] = "Villager 3",'''),
('''        "serv" or "guard" => "guard", "isis" => "servant", _ => null,''', '''        "serv" or "guard" => "guard", "isis" => "servant", "sm" => "simon", "te" => "teana", "jo" => "jono",
        "v1" => "v1", "v2" => "v2", "v3" => "v3", _ => null,'''),
])

# names
patch('FmMod.cs', [('''[ModelDb.GetEntry(typeof(Game.FmHeishin)) + ".name"] = "High Mage Heishin",''', '''[ModelDb.GetEntry(typeof(Game.FmHeishin)) + ".name"] = "High Mage Heishin", [ModelDb.GetEntry(typeof(Game.FmDuelSimon)) + ".name"] = "Simon Muran", [ModelDb.GetEntry(typeof(Game.FmDuelTeana)) + ".name"] = "Teana", [ModelDb.GetEntry(typeof(Game.FmDuelJono)) + ".name"] = "Jono", [ModelDb.GetEntry(typeof(Game.FmDuelVillager1)) + ".name"] = "Villager 1", [ModelDb.GetEntry(typeof(Game.FmDuelVillager2)) + ".name"] = "Villager 2", [ModelDb.GetEntry(typeof(Game.FmDuelVillager3)) + ".name"] = "Villager 3",''')])
print('ok')
