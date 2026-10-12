using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;
using System.Runtime.CompilerServices;

namespace FMDuelist.Game;

/// <summary>
/// Enemy tactics. Before an attack, the enemy weighs every target on your field against going into Defense Position:
///   value(attack a monster) = what destroying it is worth (its threat) + Life Point damage that spills over
///                           - the damage it would take back, weighted by how hurt it already is;
///   value(defend)           = how much of your monsters' attack its Block would soak up.
/// A hit that would kill the enemy itself is never taken. Elites and bosses are bolder, normal enemies more careful.
/// Each time in a row an enemy goes into Defense Position, defending is worth less to it (halved per streak step) and
/// it accepts a worse attack, so it can't turtle up forever against a strong board.
/// </summary>
public static class FmEnemyAI
{
    /// <summary>The monster the current enemy attack goes after (its first hit; later hits re-plan).</summary>
    public static FmSlot? Planned;

    /// <summary>How many times in a row each enemy has gone into Defense Position.</summary>
    static readonly ConditionalWeakTable<Creature, StrongBox<int>> _streak = new();

    static int Streak(Creature c) => _streak.TryGetValue(c, out var b) ? b.Value : 0;

    static void SetStreak(Creature c, int n) => _streak.AddOrUpdate(c, new StrongBox<int>(n));

    public record Plan(bool Attack, FmSlot? Target, double AttackValue, double DefendValue);

    /// <summary>How dangerous a monster is to the enemy: what it can hit for.</summary>
    static double Threat(FmSlot s) => s.Position == FmPosition.Attack ? s.Atk : s.Atk * 0.5 + s.Def * 0.15;

    /// <summary>Outcome of hitting this monster: (destroyed, damage to Life Points, damage back to the enemy).</summary>
    static (bool destroyed, int lp, int back) Outcome(Player player, FmSlot s, int hit)
    {
        if (s.Position == FmPosition.Attack)
            return hit > s.Atk ? (true, hit - s.Atk, 0)
                : hit < s.Atk ? (false, 0, (s.Atk - hit) * Relics.FmBonus.CounterMultiplier(player))
                : (true, 0, s.Atk * Relics.FmBonus.CounterMultiplier(player));
        int block = player.Creature.Block;
        return (true, 0, hit <= block ? block - hit : 0);
    }

    static double Boldness(Player player) => player.RunState.CurrentRoom?.RoomType switch
    {
        RoomType.Boss => 0.55,
        RoomType.Elite => 0.75,
        _ => 1.0,
    };

    public static Plan Decide(Creature enemy, Player player, int hit, int hits)
    {
        var living = FmField.Living(player).ToList();
        if (living.Count == 0) { SetStreak(enemy, 0); return new Plan(true, null, double.MaxValue, 0); }
        int streak = Streak(enemy);
        int enemyLife = enemy.CurrentHp + enemy.Block;
        double bold = Boldness(player);

        FmSlot? best = null; double bestValue = double.MinValue;
        foreach (var s in living)
        {
            var (destroyed, lp, back) = Outcome(player, s, hit);
            if (back >= enemyLife) continue; // never a suicidal attack
            double gain = (destroyed ? Threat(s) * 1.5 : 0) + lp * 1.2;
            double risk = back * bold * (1 + 2.0 * back / Math.Max(1, enemyLife)); // the more it would hurt, the warier it gets
            double value = gain - risk;
            // a Defense monster its hit overpowers is a clean kill: well worth taking (it barely weighs the reflected Block)
            if (s.Position == FmPosition.Defense && hit > s.Def)
                value = Math.Max(value, Threat(s) * 1.5 + hit * 0.6 + 400 - risk * 0.25);
            if (value > bestValue) { bestValue = value; best = s; }
        }

        // defending: its Block soaks up what your attack-position monsters can throw at it next turn
        double incoming = living.Where(s => s.Position == FmPosition.Attack).Sum(s => (double)s.Atk);
        double defend = Math.Min(incoming, hit * hits) * 0.8;
        // badly hurt enemies value their Block more
        defend *= 1 + 0.75 * (1 - Math.Min(1.0, enemyLife / Math.Max(1.0, enemy.MaxHp)));

        // every defend in a row makes the next one less appealing, and a less-than-ideal attack more acceptable
        defend *= Math.Pow(0.5, streak);
        double tolerance = streak * 400.0;

        // it would rather destroy a weak Defense monster than turtle up
        bool cleanKill = best != null && best.Position == FmPosition.Defense && hit > best.Def;
        bool attack = best != null && bestValue > -tolerance && (bestValue >= defend || cleanKill);
        SetStreak(enemy, attack ? 0 : streak + 1);
        Log.Info($"[FMDuelist] AI {enemy.Name}: hit {hit}x{hits}, best target {(best == null ? "none" : Data.FmData.Get(best.MonsterId).Name)} ({bestValue:0}) vs defend ({defend:0}, streak {streak}) -> {(attack ? "attack" : "defend")}");
        return new Plan(attack, best, bestValue, defend);
    }
}
