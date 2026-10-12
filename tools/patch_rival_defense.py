import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))
p = 'Game/FmRival.cs'
s = open(p, encoding='utf-8').read()
a = '''    static async Task Attacks(PlayerChoiceContext ctx, Player player)
    {
        foreach (var s in Living.Where(s => s.Position == FmPosition.Attack).OrderByDescending(s => s.Atk).ToList())
        {'''
b = '''    /// <summary>Can this monster of his win a battle against one of yours (or is your field empty)?</summary>
    static bool HasPrey(FmSlot s, Player player)
    {
        var mine = FmField.Living(player).ToList();
        return mine.Count == 0 || mine.Any(m => m.Position == FmPosition.Attack ? m.Atk < s.Atk : m.Def < s.Atk);
    }

    /// <summary>FM AI: a monster of his switches position, with a banner.</summary>
    static async Task Switch(FmSlot s, FmPosition to)
    {
        if (s.Position == to) return;
        s.Position = to;
        FmSfx.Play(FmSfx.Flip);
        FmField.NotifyChanged();
        Banner($"{FmData.Get(s.MonsterId).Name} switches to {(to == FmPosition.Attack ? "Attack" : "Defense")} position!");
        await Wait(0.35);
    }

    static async Task Attacks(PlayerChoiceContext ctx, Player player)
    {
        // monsters in Defense that now have something to beat stand up to attack
        foreach (var s in Living.Where(s => s.Position == FmPosition.Defense && !s.AttackedThisTurn).ToList())
            if (HasPrey(s, player)) await Switch(s, FmPosition.Attack);

        foreach (var s in Living.Where(s => s.Position == FmPosition.Attack).OrderByDescending(s => s.Atk).ToList())
        {
            // FM AI: once his strongest remaining monster has nothing it can beat, every monster of his that hasn't
            // attacked goes into Defense Position
            if (s.MonsterId != 0 && !s.AttackedThisTurn && !HasPrey(s, player))
            {
                foreach (var d in Living.Where(d => !d.AttackedThisTurn && d.Position == FmPosition.Attack).ToList()) await Switch(d, FmPosition.Defense);
                break;
            }
'''
assert a in s; s = s.replace(a, b, 1)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
