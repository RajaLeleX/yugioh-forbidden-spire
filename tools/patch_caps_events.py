import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:80]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# events: HP numbers in plainly named variables (HpLoss, MaxHpLoss, SmashHPGain, DrinkMaxHpGain...) show x100 too
patch('FmItemText.cs', [
('''        if (!FmRules.RunActive) return false;
        if (v is not (DamageVar or BlockVar or HealVar or HpLossVar or MaxHpVar)) return false;''', '''        if (!FmRules.RunActive) return false;
        bool hpNamed = v.GetType() == typeof(DynamicVar) && v.Name.Contains("hp", StringComparison.OrdinalIgnoreCase) && Owner(v) is EventModel;
        if (!hpNamed && v is not (DamageVar or BlockVar or HealVar or HpLossVar or MaxHpVar)) return false;'''),
])

# monsters lose every damage cap
patch('Game/FmStatRework.cs', [
('''            if (who.IsEnemy)
            {
                switch (power)
                {''', '''            if (who.IsEnemy)
            {
                if (IsDamageCap(power)) { await PowerCmd.Remove(power); return; }
                switch (power)
                {'''),
('''    static async Task Loss<T>(''', '''    /// <summary>Monster powers that cap or soak damage (they break FM's battle numbers), removed from every enemy.</summary>
    public static bool IsDamageCap(PowerModel p) => p is HardenedShellPower or SlipperyPower or HardToKillPower or IntangiblePower or BufferPower;

    /// <summary>Strips damage caps from every enemy (combat start and each turn, in case a move re-applies one).</summary>
    public static async Task StripCaps(MegaCrit.Sts2.Core.Combat.ICombatState? combat)
    {
        if (combat == null) return;
        foreach (var e in combat.Enemies.Where(e => e.IsAlive).ToList())
            foreach (var p in e.Powers.Where(IsDamageCap).ToList())
            {
                try { await PowerCmd.Remove(p); Log.Info($"[FMDuelist] removed {p.Id.Entry} from {e.Name}"); }
                catch (Exception ex) { Log.Warn("[FMDuelist] strip cap: " + ex.Message); }
            }
    }

    static async Task Loss<T>('''),
])

patch('Relics/MillenniumPuzzle.cs', [
('''        foreach (var e in Owner.Creature.CombatState!.Enemies) await ScaleEnemy(e);''', '''        foreach (var e in Owner.Creature.CombatState!.Enemies) await ScaleEnemy(e);
        await FmStatRework.StripCaps(Owner.Creature.CombatState);'''),
('''        if (side == CombatSide.Player && participants.Contains(Owner.Creature)) { FmGuardianStars.Reset(); FmField.NotifyChanged(); }
        return Task.CompletedTask;''', '''        if (side == CombatSide.Player && participants.Contains(Owner.Creature)) { FmGuardianStars.Reset(); FmField.NotifyChanged(); }
        return FmStatRework.StripCaps(combatState as MegaCrit.Sts2.Core.Combat.ICombatState ?? Owner.Creature.CombatState);'''),
])
print('ok')
