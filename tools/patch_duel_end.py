import os, re
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:90]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# ---------- music: jingle once, then loop another track ----------
patch('Game/FmMusicImpl.cs', [
('''    /// <summary>Plays as soon as the disc's music bank has loaded''', '''    /// <summary>Plays one track once, then loops another as soon as it finishes (e.g. the win jingle, then the results music).</summary>
    public static async void PlayOnceThenLoop(int first, int second)
    {
        PlayOnce(first);
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        int target = first;
        for (int i = 0; i < 200 && !(_playing == target && _player != null && GodotObject.IsInstanceValid(_player) && _player.Playing); i++)
            await tree.ToSignal(tree.CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        while (_playing == target && _player != null && GodotObject.IsInstanceValid(_player) && _player.Playing)
            await tree.ToSignal(tree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        if (_playing == target) { _once = false; Play(second); }
    }

    /// <summary>Plays as soon as the disc's music bank has loaded'''),
])

# ---------- results screen: music + optional spoils ----------
patch('Game/FmDuelResults.cs', [
('''    public static async Task Show(bool won, Player player, int comLp, string comName, int turns)
    {''', '''    /// <summary>FM's own music for the end of a duel: the win jingle, the lose jingle, and the Results of Duel theme.</summary>
    public const int WinJingle = 46, LoseJingle = 47, ResultsTrack = 39;

    public static async Task Show(bool won, Player player, int comLp, string comName, int turns, bool spoils = true)
    {
        FmMusic.PlayOnceThenLoop(won ? WinJingle : LoseJingle, ResultsTrack);'''),
('''        int chips = won ? stars * 10 : 0;''', '''        int chips = won && spoils ? stars * 10 : 0;'''),
])

# ---------- the duel: stats ----------
p = 'Game/FmRival.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''        foreach (var b in Bosses)
            if (combat.Enemies.FirstOrDefault(e => e.Monster != null && e.Monster.GetType() == b.Monster) is { } boss) { Priest = boss; Current = b; break; }''', '''        foreach (var b in AllDuelists)
            if (combat.Enemies.FirstOrDefault(e => e.Monster != null && e.Monster.GetType() == b.Monster) is { } boss) { Priest = boss; Current = b; break; }''', 1)
s = s.replace('''    public static Duelist? Current;''', '''    /// <summary>The bosses plus the main menu's practice opponent.</summary>
    public static IEnumerable<Duelist> AllDuelists => Bosses.Append(FmPractice.Duelist);
    public static bool IsDuelist(Creature? c) => c?.Monster != null && AllDuelists.Any(b => b.Monster == c.Monster.GetType());

    public static Duelist? Current;''', 1)
s = s.replace('''        Active = true;
        // the boss's own buffs''', '''        Active = true;
        FmDuelStats.Reset();
        if (Current == FmPractice.Duelist && NCombatRoom.Instance?.GetCreatureNode(Priest) is { } mirror)
            mirror.Visuals.Scale = new Vector2(-Math.Abs(mirror.Visuals.Scale.X), mirror.Visuals.Scale.Y); // the mirror Duel Master K faces you
        // the boss's own buffs''', 1)
s = s.replace('''    static void BuildDeck()
    {
        int index = Current?.DuelistIndex ?? 7;''', '''    static void BuildDeck()
    {
        if (Current == FmPractice.Duelist) { Deck.Clear(); Deck.AddRange(FmPractice.Deck(FmPractice.Level)); return; }
        int index = Current?.DuelistIndex ?? 7;''', 1)
# his cards used
s = s.replace('''        if (used.Count == 0) return;
        foreach (var i in used.OrderByDescending(i => i)) Hand.RemoveAt(i);''', '''        if (used.Count == 0) return;
        FmDuelStats.Com.CardsUsed += used.Count;
        foreach (var i in used.OrderByDescending(i => i)) Hand.RemoveAt(i);''', 1)
# battles
s = s.replace('''        int a = att.Atk;
        Creature attOwner = playerAttacks ? player.Creature : Priest!, defOwner = playerAttacks ? Priest! : player.Creature;''', '''        int a = att.Atk;
        Creature attOwner = playerAttacks ? player.Creature : Priest!, defOwner = playerAttacks ? Priest! : player.Creature;
        int defValue = def.Position == FmPosition.Attack ? def.Atk : def.Def, defHp = defOwner.CurrentHp;
        int attId = att.MonsterId, defId = def.MonsterId;''', 1)
s = s.replace('''            else if (a < d) await Lp(ctx, attOwner, d - a);
        }
        FmField.NotifyChanged();''', '''            else if (a < d) await Lp(ctx, attOwner, d - a);
        }
        FmDuelStats.Battle(playerAttacks, a, defValue, def.MonsterId != defId || def.MonsterId == 0, att.MonsterId != attId || att.MonsterId == 0, defOwner.CurrentHp < defHp);
        FmField.NotifyChanged();''', 1)
s = s.replace('''        else if (target == Priest && !HasMonster) await Lp(ctx, Priest, mine.Atk);''', '''        else if (target == Priest && !HasMonster) { FmDuelStats.DirectAttack(true, mine.Atk); await Lp(ctx, Priest, mine.Atk); }''', 1)
s = s.replace('''                await FmAttackVfx.Play(s.MonsterId, from, to);
                await Lp(ctx, player.Creature, s.Atk);''', '''                await FmAttackVfx.Play(s.MonsterId, from, to);
                FmDuelStats.DirectAttack(false, s.Atk);
                await Lp(ctx, player.Creature, s.Atk);''', 1)
# your traps springing on his attacks
s = s.replace('''            FmField.Traps.RemoveAt(i);
            FmSfx.Play(FmSfx.Trap);
            Banner($"Your {FmData.Get(id).Name} destroys''', '''            FmField.Traps.RemoveAt(i);
            FmDuelStats.Traps++;
            FmSfx.Play(FmSfx.Trap);
            Banner($"Your {FmData.Get(id).Name} destroys''', 1)
open(p, 'w', encoding='utf-8').write(s)

# ---------- your plays ----------
patch('Cards/FmMonsterCard.cs', [
('''    public static async Task<int> Resolve(PlayerChoiceContext ctx, List<CardModel> chain, Player owner, CardModel played, int fieldMonster)
    {''', '''    public static async Task<int> Resolve(PlayerChoiceContext ctx, List<CardModel> chain, Player owner, CardModel played, int fieldMonster)
    {
        if (FmRival.Active && chain.Count > 1) { FmDuelStats.Fusions++; FmDuelStats.You.CardsUsed += chain.Count - 1; }'''),
])

p = 'Relics/MillenniumPuzzle.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''FmRival.Bosses.Any(b => b.Monster == e.Monster.GetType())''', '''FmRival.AllDuelists.Any(b => b.Monster == e.Monster.GetType())''', 1)
anchor = '''    public override Task AfterCardGeneratedForCombat('''
add = '''    // --- Forbidden Memories duel bookkeeping and the end of a duel ---
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (FmRival.Active && cardPlay.Card is FmCardBase fc && cardPlay.Card.Owner == Owner)
        {
            FmDuelStats.You.CardsUsed++;
            if (fc is FmSpellCard { Kind: FmSpellKind.Equip }) FmDuelStats.Equips++;
            else if (fc is FmSpellCard) FmDuelStats.Magic++;
        }
        return Task.CompletedTask;
    }

    public override bool ShouldDie(Creature creature)
    {
        // the practice duel never ends the run: whoever falls, the results show and you go back to the main menu
        if (!FmPractice.Active || !FmRival.Active) return true;
        if (creature == Owner.Creature || creature == FmRival.Priest)
        {
            bool won = creature == FmRival.Priest;
            int turns = Owner.Creature.CombatState?.RoundNumber ?? 1;
            int comLp = Math.Max(0, FmRival.Priest?.CurrentHp ?? 0);
            FmRival.Active = false;
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(FmPractice.Finish(won, Owner, won ? 0 : comLp, turns));
            return false;
        }
        return true;
    }

    public override async Task BeforeDeath(Creature creature)
    {
        if (!FmRival.Active || FmPractice.Active || FmRival.Current == null) return;
        if (creature != Owner.Creature && creature != FmRival.Priest) return;
        bool won = creature == FmRival.Priest;
        int turns = Owner.Creature.CombatState?.RoundNumber ?? 1;
        int comLp = won ? 0 : Math.Max(0, FmRival.Priest?.CurrentHp ?? 0);
        await FmDuelEnd.Show(won, Owner, comLp, FmRival.Current.Name, turns);
    }

'''
assert anchor in s; s = s.replace(anchor, add + anchor, 1)
# the boss fanfare is now the win jingle in the results sequence
s = s.replace('''        if (room.RoomType == RoomType.Boss) FmMusic.PlayOnce(FmMedia.BossFanfareTrack);''', '''        // the boss duel's win jingle and results music play in FmDuelEnd''', 1)
open(p, 'w', encoding='utf-8').write(s)

# ---------- no tutorial pop-up in the practice duel ----------
p = 'Game/FmTutorial.cs'
s = open(p, encoding='utf-8').read()
a = '''            if (rs.CurrentActIndex != 0 || player.Creature.CombatState?.RoundNumber != 1) return;'''
assert a in s; s = s.replace(a, '''            if (FmPractice.Active || rs.CurrentActIndex != 0 || player.Creature.CombatState?.RoundNumber != 1) return;''', 1)
open(p, 'w', encoding='utf-8').write(s)

# ---------- names ----------
patch('FmMod.cs', [('''__instance.GetTable("main_menu_ui").MergeWith(new Dictionary<string, string> { ["TIMELINE"] = "Library" });''',
 '''__instance.GetTable("main_menu_ui").MergeWith(new Dictionary<string, string> { ["TIMELINE"] = "Library", ["COMPENDIUM"] = "Duel the Duel Master K" }); __instance.GetTable("monsters").MergeWith(new Dictionary<string, string> { [ModelDb.GetEntry(typeof(Game.FmMirrorDK)) + ".name"] = "Duel Master K", [ModelDb.GetEntry(typeof(Game.FmMirrorDK)) + ".moves.FM_DUEL_MOVE.title"] = "Duel" }); __instance.GetTable("encounters").MergeWith(new Dictionary<string, string> { [ModelDb.GetEntry(typeof(Game.FmPracticeEncounter)) + ".title"] = "Duel the Duel Master K", [ModelDb.GetEntry(typeof(Game.FmPracticeEncounter)) + ".loss"] = "Duel Master K" });''')])
print('ok')
