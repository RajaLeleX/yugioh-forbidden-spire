import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:70]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Game/FmEnemyDefense.cs', [
('''/// Block equal to the damage it would have dealt.
/// </summary>''', '''/// Block equal to the damage it would have dealt. Only the attack is replaced: the rest of its move (statuses, debuffs,
/// buffs, cards added to your piles, summons) still happens, with a "still uses" message so it's clear.
/// </summary>'''),
('''    static async Task<AttackCommand> Defend(Creature enemy, int block, AttackCommand cmd)
    {''', '''    /// <summary>The enemy that went into Defense Position during this enemy turn (its move carries on).</summary>
    static Creature? _defender;
    static int _defendRound = -1;
    static readonly HashSet<(Creature, string)> _announced = new();

    public static Creature? DefenderNow(ICombatState? combat)
        => _defender != null && combat != null && combat.RoundNumber == _defendRound && combat.CurrentSide == CombatSide.Enemy ? _defender : null;

    /// <summary>"Name still uses X!" once per thing per turn, for the parts of its move that aren't the attack.</summary>
    public static void StillUses(Creature enemy, string what)
    {
        if (!_announced.Add((enemy, what))) return;
        Log.Info($"[FMDuelist] {enemy.Name} defends but still uses {what}");
        _ = Banner(enemy, $"{enemy.Name} still uses {what}!", 60);
    }

    static async Task<AttackCommand> Defend(Creature enemy, int block, AttackCommand cmd)
    {
        if (enemy.CombatState is { } cs && (cs.RoundNumber != _defendRound || _defender != enemy)) _announced.Clear();
        _defender = enemy; _defendRound = enemy.CombatState?.RoundNumber ?? -1;'''),
('''    static Task? Banner(Creature enemy, string text)
    {''', '''    static Task? Banner(Creature enemy, string text, float lower = 0)
    {'''),
('''            Position = new Vector2(rect.GetCenter().X - 280, rect.Position.Y - 70), Modulate = new Color(1, 1, 1, 0),''',
 '''            Position = new Vector2(rect.GetCenter().X - 280, rect.Position.Y - 70 + lower), Modulate = new Color(1, 1, 1, 0),'''),
])

patch('Relics/MillenniumPuzzle.cs', [
('''        await FmGuardianStars.OnPowerChanged(Owner, power, amount);
        await FmStatRework.OnPowerChanged(Owner, power, amount);''', '''        await FmGuardianStars.OnPowerChanged(Owner, power, amount);
        await FmStatRework.OnPowerChanged(Owner, power, amount);
        // an enemy in Defense Position still carries out the rest of its move
        if (amount > 0 && applier != null && applier == EnemyDefensePatch.DefenderNow(applier.CombatState))
            EnemyDefensePatch.StillUses(applier, power.Title.GetFormattedText());'''),
])
s = open('Relics/MillenniumPuzzle.cs', encoding='utf-8').read()
anchor = '''    public override Task BeforeSideTurnStart('''
add = '''    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (creator == null && card.Owner == Owner && EnemyDefensePatch.DefenderNow(Owner.Creature.CombatState) is { } d)
            EnemyDefensePatch.StillUses(d, card.Title);
        return Task.CompletedTask;
    }

'''
assert anchor in s; s = s.replace(anchor, add + anchor, 1)
open('Relics/MillenniumPuzzle.cs', 'w', encoding='utf-8').write(s)
print('ok')
