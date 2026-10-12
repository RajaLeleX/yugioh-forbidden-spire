import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:90]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Game/FmRival.cs', [
('''    public static IEnumerable<Duelist> AllDuelists => Bosses.Append(FmPractice.Duelist);''',
 '''    public static IEnumerable<Duelist> AllDuelists => Bosses.Append(FmPractice.Duelist).Append(FmAct1Story.Heishin);'''),
('''            if (combat.Enemies.FirstOrDefault(e => e.Monster != null && e.Monster.GetType() == b.Monster) is { } boss) { Priest = boss; Current = b; break; }''',
 '''            if (combat.Enemies.FirstOrDefault(e => e.IsAlive && e.Monster != null && e.Monster.GetType() == b.Monster) is { } boss) { Priest = boss; Current = b; break; }'''),
# the unwinnable duel can't drag on forever: after 12 turns Heishin unleashes the forbidden power
('''        if (Bound)
        {''', '''        if (Current == FmAct1Story.Heishin && FmAct1Story.TurnsIntoDuel(combat) >= 12 && player.Creature.CurrentHp > 0)
        {
            Banner($"{Name} unleashes the forbidden power of darkness!");
            try { await FmSpellVfx.DarkHole(); } catch { }
            await Lp(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), player.Creature, player.Creature.CurrentHp);
            return;
        }
        if (Bound)
        {'''),
# his attacks stop as soon as the duel is over
('''            if (Priest == null || Priest.IsDead || player.Creature.IsDead || s.MonsterId == 0) break;''',
 '''            if (!Active || Priest == null || Priest.IsDead || player.Creature.IsDead || player.Creature.CurrentHp <= 0 || s.MonsterId == 0) break;'''),
])

p = 'Relics/MillenniumPuzzle.cs'
patch(p, [
# before the Act 1 boss: the plaza and duel ground scenes
('''        // boss duel: the Kin Priest duels by FM rules''', '''        // Act 1 boss: Forbidden Memories' plaza and duel ground scenes first
        if (FmAct1Story.Enabled(Owner) && Owner.Creature.CombatState!.Enemies.Any(e => e.Monster is MegaCrit.Sts2.Core.Models.Monsters.KinPriest))
        {
            FmAct1Story.Reset();
            try { await FmAct1Story.PreBoss(); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] pre-boss scene: " + ex.Message); }
            FmMusic.OnRoomEntered(Owner.RunState.CurrentRoom!, Owner);
        }
        // boss duel: the Kin Priest duels by FM rules'''),
# Heishin can't be beaten: his Life Points never drop below 1
('''    public override decimal ModifyHpLostBeforeOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {''', '''    public override decimal ModifyHpLostBeforeOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (FmAct1Story.IsHeishin(target)) return Math.Min(amount, Math.Max(0, target.CurrentHp - 1));'''),
# the end of a duel
('''    public override async Task BeforeDeath(Creature creature)
    {
        if (!FmRival.Active || FmPractice.Active || FmRival.Current == null) return;''', '''    public override async Task BeforeDeath(Creature creature)
    {
        // Act 1 story: beating Seto leads into Heishin's invasion; losing to Heishin leads into the Puzzle
        if (FmRival.Active && !FmPractice.Active && FmRival.Current == FmRival.Bosses[0] && creature == FmRival.Priest && FmAct1Story.Enabled(Owner))
        {
            await FmAct1Story.AfterSeto(Owner, Owner.Creature.CombatState?.RoundNumber ?? 1);
            return;
        }
        if (FmAct1Story.State == FmAct1Story.Phase.HeishinDuel && creature == Owner.Creature)
        {
            await FmAct1Story.AfterHeishin(Owner);
            return;
        }
        if (!FmRival.Active || FmPractice.Active || FmRival.Current == null) return;'''),
('''        // the practice duel never ends the run: whoever falls, the results show and you go back to the main menu''',
 '''        // the unwinnable Heishin duel isn't a game over
        if (creature == Owner.Creature && FmAct1Story.State == FmAct1Story.Phase.Lost) return false;
        if (FmAct1Story.IsHeishin(creature) && FmAct1Story.State != FmAct1Story.Phase.None) return false;
        // the practice duel never ends the run: whoever falls, the results show and you go back to the main menu'''),
# Seto has fallen: on to Heishin
('''            foreach (var z in FmRival.Slots) if (z.RivalCreature is { IsAlive: true } zc) await CreatureCmd.Kill(zc);
            FmRival.Active = false;
        }''', '''            foreach (var z in FmRival.Slots) if (z.RivalCreature is { IsAlive: true } zc) await CreatureCmd.Kill(zc);
            FmRival.Active = false;
        }
        if (FmAct1Story.State == FmAct1Story.Phase.HeishinPending && creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.KinPriest)
            await FmAct1Story.StartHeishin(ctx, Owner);'''),
('''        FmField.ResetCombat();
        FmRival.Reset();
        return Task.CompletedTask;''', '''        FmField.ResetCombat();
        FmRival.Reset();
        FmAct1Story.Reset();
        return Task.CompletedTask;'''),
])

# names
patch('FmMod.cs', [('''[ModelDb.GetEntry(typeof(Game.FmMirrorDK)) + ".moves.FM_DUEL_MOVE.title"] = "Duel" });''',
 '''[ModelDb.GetEntry(typeof(Game.FmMirrorDK)) + ".moves.FM_DUEL_MOVE.title"] = "Duel", [ModelDb.GetEntry(typeof(Game.FmHeishin)) + ".name"] = "High Mage Heishin", [ModelDb.GetEntry(typeof(Game.FmHeishin)) + ".moves.FM_DUEL_MOVE.title"] = "Duel" });''')])
print('ok')
