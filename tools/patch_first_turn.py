import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:70]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Game/FmRival.cs', [
('''    public static void Banner(string text)''', '''    /// <summary>FM duel rule: whoever goes first can't attack on that first turn.</summary>
    public static bool FirstTurnNoAttack => Active && Priest?.CombatState is { } cs && cs.RoundNumber <= 1;

    static int _firstTurnWarned = -1;

    /// <summary>True (with a reminder) if attacking is blocked by the first-turn rule.</summary>
    public static bool BlockFirstTurnAttack()
    {
        if (!FirstTurnNoAttack) return false;
        if (_firstTurnWarned != Priest!.CombatState!.RoundNumber || true)
        {
            _firstTurnWarned = Priest.CombatState.RoundNumber;
            Banner("You go first: no attacking on the first turn of a duel!");
            FmSfx.Play(FmSfx.Flip);
        }
        return true;
    }

    public static void Banner(string text)'''),
])
patch('Game/FmField.cs', [
('''        if (!slot.Alive(player) || slot.Position != FmPosition.Attack || slot.AttackedThisTurn) return;
        var combat = player.Creature.CombatState;''', '''        if (!slot.Alive(player) || slot.Position != FmPosition.Attack || slot.AttackedThisTurn) return;
        if (FmRival.BlockFirstTurnAttack()) return; // FM: the player going first can't attack on turn 1
        var combat = player.Creature.CombatState;'''),
])
patch('Game/FmUi.cs', [
('''        if (zone.Position != FmPosition.Attack || zone.AttackedThisTurn) return false;
        _ = AimAndAttack(player, zone);''', '''        if (zone.Position != FmPosition.Attack || zone.AttackedThisTurn) return false;
        if (FmRival.BlockFirstTurnAttack()) return true;
        _ = AimAndAttack(player, zone);'''),
('''            : _slot.AttackedThisTurn ? "ATK  (attacked - locked)" : "ATK  click: attack / right-click: DEF";''',
 '''            : _slot.AttackedThisTurn ? "ATK  (attacked - locked)"
            : FmRival.FirstTurnNoAttack ? "ATK  (no attacks on turn 1)" : "ATK  click: attack / right-click: DEF";'''),
])
patch('Game/FmTutorial.cs', [
('''        "- They follow your rules: a hand of 5, one summon per turn, and they always make the strongest fusion they can.",''',
 '''        "- [b]You go first, so you can't attack on your first turn[/b] (just like a Forbidden Memories duel). Summon, fuse, equip and set traps instead.",
        "- They show 5 face-down cards, but like Forbidden Memories duelists they secretly choose from a hidden pool of up to 20. One summon per turn, and they always make the strongest fusion they can.",'''),
('''        "- Your traps can spring on his attacks, and Swords of Revealing Light stops him attacking.",''',
 '''        "- Your traps can spring on his attacks, and he sets traps of his own. Swords of Revealing Light stops him attacking for 3 turns; Spellbinding Circle makes him skip a turn.",'''),
])
print('ok')
