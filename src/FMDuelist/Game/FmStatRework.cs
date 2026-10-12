using System.Text.RegularExpressions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using FMDuelist.Powers;

namespace FMDuelist.Game;

/// <summary>
/// StS status effects that clash with Forbidden Memories rules become FM-style effects as soon as they're applied:
///   Weak on an enemy       -> it loses ATK (500 / 750 / 1000 for 1 / 2 / 3+ stacks)
///   Vulnerable on an enemy -> it loses DEF (same scale)
///   Poison on an enemy     -> Burn: 100 damage per stack at the start of its turn, then one stack fades
///   Thorns on you          -> Counter Trap: an enemy that attacks you or your monsters takes 100 per stack
///   Vigor / Focus on you   -> a Sun Star (+500 ATK for your monsters until your next turn)
/// Enemies' own debuffs on you are left as they are.
/// </summary>
public static class FmStatRework
{
    public static int LossFor(decimal stacks) => (int)Math.Clamp(250 + 250 * stacks, 500, 1000);

    public static async Task OnPowerChanged(Player owner, PowerModel power, decimal amount)
    {
        var who = power.Owner;
        if (who == null || amount <= 0) return;
        var ctx = new BlockingPlayerChoiceContext();
        try
        {
            if (who.IsEnemy)
            {
                switch (power)
                {
                    case WeakPower:
                        await PowerCmd.Remove(power);
                        await Loss<FmAtkLossPower>(ctx, who, LossFor(amount), owner, atk: true, def: false);
                        break;
                    case VulnerablePower:
                        await PowerCmd.Remove(power);
                        await Loss<FmDefLossPower>(ctx, who, LossFor(amount), owner, atk: false, def: true);
                        break;
                    case PoisonPower:
                        await PowerCmd.Remove(power);
                        await PowerCmd.Apply<FmBurnPower>(ctx, who, amount, owner.Creature, null);
                        break;
                }
            }
            else if (who == owner.Creature)
            {
                switch (power)
                {
                    case ThornsPower:
                        await PowerCmd.Remove(power);
                        await PowerCmd.Apply<FmCounterTrapPower>(ctx, who, amount, who, null);
                        break;
                    case VigorPower or FocusPower:
                        await PowerCmd.Remove(power);
                        FmGuardianStars.TurnAtk += FmGuardianStars.AtkBonus;
                        foreach (var s in FmField.Living(owner)) s.Visual.StarFlash(true);
                        FmField.NotifyChanged();
                        break;
                }
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] stat rework: " + ex.Message); }
    }

    static async Task Loss<T>(PlayerChoiceContext ctx, Creature enemy, int value, Player owner, bool atk, bool def) where T : PowerModel
    {
        if (FmRival.SlotOf(enemy) is { } zone)
        {
            if (atk) zone.AtkBonus -= value;
            if (def) zone.DefBonus -= value;
            FmField.NotifyChanged();
            return;
        }
        await PowerCmd.Apply<T>(ctx, enemy, value, owner.Creature, null);
    }

    // ---------------- text ----------------
    const string Num = @"(?:\[blue\])?\{[A-Za-z]+(?::[^}]*)?\}(?:\[/blue\])?|(?:\[blue\])?\d+(?:\[/blue\])?";
    static readonly (Regex re, string to)[] Rules =
    {
        (new($@"(?:{Num})\s+(?:\[gold\])?Weak(?:\[/gold\])?"), "[gold]ATK loss[/gold] ([blue]500-1000[/blue] ATK)"),
        (new($@"(?:{Num})\s+(?:\[gold\])?Vulnerable(?:\[/gold\])?"), "[gold]DEF loss[/gold] ([blue]500-1000[/blue] DEF)"),
        (new($@"(?:{Num})\s+(?:\[gold\])?Poison(?:\[/gold\])?"), "[gold]Burn[/gold] ([blue]100[/blue] damage per stack each enemy turn)"),
        (new($@"(?:{Num})\s+(?:\[gold\])?Thorns(?:\[/gold\])?"), "a [gold]Counter Trap[/gold] ([blue]100[/blue] damage per stack to each attacker)"),
        (new($@"(?:{Num})\s+(?:\[gold\])?(?:Vigor|Focus)(?:\[/gold\])?"), "a [gold]Sun Star[/gold] ([blue]+500[/blue] ATK for your monsters until your next turn)"),
        (new(@"\[gold\]Weak\[/gold\]"), "[gold]ATK loss[/gold]"),
        (new(@"\[gold\]Vulnerable\[/gold\]"), "[gold]DEF loss[/gold]"),
        (new(@"\[gold\]Poison(ed)?\[/gold\]"), "[gold]Burn[/gold]"),
        (new(@"\[gold\]Thorns\[/gold\]"), "[gold]Counter Trap[/gold]"),
        (new(@"\[gold\](?:Vigor|Focus)\[/gold\]"), "[gold]Sun Star[/gold]"),
    };

    static string Rewrite(string v)
    {
        foreach (var (re, to) in Rules) v = re.Replace(v, to);
        return v;
    }

    public static void ApplyLoc(LocManager loc)
    {
        try
        {
            foreach (var name in new[] { "relics", "potions" })
            {
                var t = loc.GetTable(name);
                var tr = AccessTools.FieldRefAccess<LocTable, Dictionary<string, string>>("_translations")(t);
                var upd = new Dictionary<string, string>();
                foreach (var (k, v) in tr)
                    if ((k.EndsWith(".description") || k.EndsWith(".eventDescription")) && Rewrite(v) is var r && r != v) upd[k] = r;
                t.MergeWith(upd);
            }
            string E(Type t) => ModelDb.GetEntry(t);
            var p = new Dictionary<string, string>();
            void Power(Type t, string title, string desc) { p[E(t) + ".title"] = title; p[E(t) + ".description"] = desc; p[E(t) + ".smartDescription"] = desc; }
            Power(typeof(FmAtkLossPower), "ATK Loss", "Lost ATK: its attacks deal this much less damage.");
            Power(typeof(FmDefLossPower), "DEF Loss", "Lost DEF: its Block gains are this much smaller.");
            Power(typeof(FmBurnPower), "Burn", "At the start of its turn it takes 100 damage per stack, then one stack fades.");
            Power(typeof(FmCounterTrapPower), "Counter Trap", "Whenever an enemy attacks you or your monsters, it takes 100 damage per stack.");
            loc.GetTable("powers").MergeWith(p);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] stat rework loc: " + ex.Message); }
    }
}
