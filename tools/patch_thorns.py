import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:80]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Relics/MillenniumPuzzle.cs', [
('''    public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props, Creature? dealer)
    {
        if (dealer == null || !dealer.IsEnemy || !props.IsPoweredAttack()) return target;''', '''    /// <summary>Damage an enemy's Thorns deal back to whoever attacked it.</summary>
    private static bool IsThorns(Creature? dealer, ValueProp props) =>
        dealer != null && dealer.IsEnemy && !props.IsPoweredAttack() && dealer.GetPower<ThornsPower>() != null;

    public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props, Creature? dealer)
    {
        // Thorns: when your monster attacks a thorny enemy, the thorns hurt you (your Life Points), not the monster
        if (IsThorns(dealer, props) && IsZone(target) && target.PetOwner == Owner) return Owner.Creature;
        if (dealer == null || !dealer.IsEnemy || !props.IsPoweredAttack()) return target;'''),
('''        if (target == Owner.Creature && enemyHit && FmField.HasMonster(Owner) && !props.IsPoweredAttack()) return 0;''',
 '''        if (target == Owner.Creature && IsThorns(dealer, props)) return amount; // Thorns go straight to your Life Points
        if (target == Owner.Creature && enemyHit && FmField.HasMonster(Owner) && !props.IsPoweredAttack()) return 0;'''),
])
s = open('Relics/MillenniumPuzzle.cs', encoding='utf-8').read()
if 'using MegaCrit.Sts2.Core.Models.Powers;' not in s:
    s = 'using MegaCrit.Sts2.Core.Models.Powers;\n' + s
    open('Relics/MillenniumPuzzle.cs', 'w', encoding='utf-8').write(s)

patch('Game/FmTutorial.cs', [
('''        "- Right-click a monster on the field to switch between Attack and Defense.",''',
 '''        "- Right-click a monster on the field to switch between Attack and Defense.",
        "- [b]Thorns:[/b] when one of your monsters attacks an enemy with Thorns, the thorns hit [b]you directly[/b], not your monster: you lose 100 Life Points per Thorns stack (3 Thorns = 300 LP).",'''),
])

# the Thorns tooltip on enemies says the same
patch('Game/FmStatRework.cs', [
('''            Power(typeof(FmCounterTrapPower), "Counter Trap", "Whenever an enemy attacks you or your monsters, it takes 100 damage per stack.");''',
 '''            Power(typeof(FmCounterTrapPower), "Counter Trap", "Whenever an enemy attacks you or your monsters, it takes 100 damage per stack.");
            Power(typeof(ThornsPower), "Thorns", "When one of your monsters attacks this enemy, the thorns hit you directly: you lose 100 Life Points per stack.");'''),
])
print('ok')
