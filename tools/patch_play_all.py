import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))
p = 'Game/FmRival.cs'
s = open(p, encoding='utf-8').read()

a = '''            // Forbidden Memories: one play per turn (a fusion chain counts as one), picked by priority
            await PlayOne(ctx, player);'''
b = '''            // he plays as many cards as he likes, like you: spells, a summon, equips and traps
            await PlayTurn(ctx, player);'''
assert a in s; s = s.replace(a, b, 1)

# replace the PlayOne method with PlayTurn
i = s.index('    /// <summary>\n    /// His one play this turn')
j = s.index('    /// <summary>His strongest summon from hand')
s = s[:i] + '''    /// <summary>
    /// His turn's plays, in order (he can make all of them in one turn, just like you):
    ///   heal when hurt; Raigeki / Dark Hole when your field outclasses his; burn spells; his best monster or fusion
    ///   (one summon per turn, like yours); equips that help (winning equips first); traps set face down (up to 3).
    /// </summary>
    static async Task PlayTurn(PlayerChoiceContext ctx, Player player)
    {
        var mine = FmField.Living(player).ToList();

        // heal when hurt
        if (Priest!.CurrentHp <= LifePoints * 0.55 && Hand.FirstOrDefault(id => Spell(id)?.Kind == FmSpellKind.Heal) is int heal and > 0)
        {
            await UseCard(heal);
            Banner($"{Name} plays {FmData.Get(heal).Name}!");
            await FmHealVfx.Play(heal, Priest);
            await FmRules.Raw(() => CreatureCmd.Heal(Priest, Spell(heal)!.Value));
            await Wait(0.6);
        }
        // Raigeki: wipe your field when you're ahead on monsters
        mine = FmField.Living(player).ToList();
        if (Hand.Contains(337) && mine.Count > 0 && (mine.Count >= 2 || mine.Max(m => m.Atk) > (Living.Any() ? Living.Max(m => m.Atk) : 0)))
        {
            await UseCard(337);
            Banner($"{Name} plays Raigeki!");
            try { await FmSpellVfx.Raigeki(mine.Select(m => m.Creature(player)).OfType<Creature>()); } catch { }
            foreach (var m in mine) await DestroyMine(m, player);
            await Wait(0.5);
        }
        // Dark Hole only when your field is clearly stronger than his
        mine = FmField.Living(player).ToList();
        if (Hand.Contains(336) && mine.Count > 0 && mine.Sum(m => m.Atk) > Living.Sum(m => m.Atk) + 1000)
        {
            await UseCard(336);
            Banner($"{Name} plays Dark Hole!");
            try { await FmSpellVfx.DarkHole(); } catch { }
            foreach (var m in mine) await DestroyMine(m, player);
            await DestroyAll();
            await Wait(0.5);
        }
        // burn spells go straight at your Life Points
        foreach (var burn in Hand.Where(id => Spell(id)?.Kind is FmSpellKind.Burn or FmSpellKind.BurnSelfDmg).ToList())
        {
            await UseCard(burn);
            Banner($"{Name} plays {FmData.Get(burn).Name}!");
            await Lp(ctx, player.Creature, Spell(burn)!.Value);
            await Wait(0.4);
        }
        // one summon (his strongest monster or fusion)
        if (BestSummon() is { } plan) await Summon(player, plan);
        // equips: the ones that win a battle first, then any that fit
        while (await Equip(player, needWin: true)) { }
        while (await Equip(player, needWin: false)) { }
        // traps face down
        while (await SetTrap()) { }
    }

''' + s[j:]
open(p, 'w', encoding='utf-8').write(s)

p = 'Game/FmTutorial.cs'
t = open(p, encoding='utf-8').read()
a = '''        "- Like Forbidden Memories duelists, they make [b]one play per turn[/b] (a fusion counts as one): a heal when hurt, Raigeki or Dark Hole when your field is stronger, a monster that beats yours, an equip, a burn spell or a face-down trap.",'''
b = '''        "- They play cards just like you: heals when hurt, Raigeki or Dark Hole when your field is stronger, burn spells, one summon (their strongest monster or fusion), equips and face-down traps, all in the same turn.",'''
assert a in t; t = t.replace(a, b, 1)
open(p, 'w', encoding='utf-8').write(t)
print('ok')
