using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace FMDuelist.Game;

/// <summary>
/// Guardian Stars. In FM runs, gaining Strength or Dexterity becomes a flat Guardian Star bonus for all your monsters
/// until your next turn: Sun Star = +500 ATK, Moon Star = +500 DEF (like a favored Guardian Star in Forbidden
/// Memories). The game's own Strength/Dexterity is removed right away, so it never changes damage or Block itself.
/// Items that grant them show planet-symbol icons.
/// </summary>
public static partial class FmGuardianStars
{
    public static int TurnAtk, TurnDef;

    public static void Reset() { TurnAtk = TurnDef = 0; }

    /// <summary>Called for every power change on the FM player.</summary>
    public static async Task OnPowerChanged(Player owner, PowerModel power, decimal amount)
    {
        if (power.Owner != owner.Creature) return;
        bool atk = power is StrengthPower, def = power is DexterityPower;
        if (!atk && !def) return;
        // losses (e.g. "lose it at end of turn") just clear the game's own power: the star bonus runs until your next turn
        if (amount <= 0) { if (power.Amount != 0) await PowerCmd.Remove(power); return; }
        if (atk) TurnAtk += AtkBonus; else TurnDef += DefBonus;
        FmField.NotifyChanged();
        Log.Info($"[FMDuelist] Guardian Star: +{(atk ? AtkBonus : DefBonus)} {(atk ? "ATK" : "DEF")} this turn");
        foreach (var s in FmField.Living(owner)) s.Visual.StarFlash(atk);
        await PowerCmd.Remove(power);
    }

    // ---------------- text ----------------
    const string Sun = "[gold]Sun Star[/gold] (your monsters get [blue]+500[/blue] ATK until your next turn)";
    const string Moon = "[gold]Moon Star[/gold] (your monsters get [blue]+500[/blue] DEF until your next turn)";
    static readonly Regex StrPhrase = new(@"\{[A-Za-z]+(?::[^}]*)?\}\s+(?:additional\s+)?(?:\[gold\])?Strength(?:\[/gold\])?");
    static readonly Regex DexPhrase = new(@"\{[A-Za-z]+(?::[^}]*)?\}\s+(?:additional\s+)?(?:\[gold\])?Dexterity(?:\[/gold\])?");
    static readonly Regex BareStr = new(@"(?<![\[/{A-Za-z])(?:\[gold\])?Strength(?:\[/gold\])?(?![A-Za-z}\]:])");
    static readonly Regex BareDex = new(@"(?<![\[/{A-Za-z])(?:\[gold\])?Dexterity(?:\[/gold\])?(?![A-Za-z}\]:])");

    static string Rewrite(string v)
    {
        // only the parts about you: "ALL enemies gain X Strength" stays as it is
        var parts = Regex.Split(v, @"(?<=\.)\s+|\s+(?=and ALL enemies)");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Contains("enem", StringComparison.OrdinalIgnoreCase) || parts[i].Contains("Elite")) continue;
            var p = StrPhrase.Replace(parts[i], Sun);
            p = DexPhrase.Replace(p, Moon);
            p = BareStr.Replace(p, "[gold]Sun Star[/gold]");
            p = BareDex.Replace(p, "[gold]Moon Star[/gold]");
            parts[i] = p;
        }
        return string.Join(" ", parts);
    }

    public static void ApplyLoc(LocManager loc)
    {
        try
        {
            void Table(string name, IEnumerable<string> keys)
            {
                var t = loc.GetTable(name);
                var tr = AccessTools.FieldRefAccess<LocTable, Dictionary<string, string>>("_translations")(t);
                var upd = new Dictionary<string, string>();
                foreach (var key in keys)
                    foreach (var field in new[] { ".description", ".eventDescription" })
                        if (tr.TryGetValue(key + field, out var v)) upd[key + field] = Rewrite(v);
                t.MergeWith(upd);
            }
            Table("relics", RelicIcons.Keys);
            Table("potions", PotionIcons.Keys);
            // potions whose "lose it at end of turn" no longer matters (the bonus is for this turn anyway)
            loc.GetTable("potions").MergeWith(new Dictionary<string, string>
            {
                ["FLEX_POTION.description"] = "Your monsters gain [blue]+500[/blue] ATK until your next turn ([gold]Sun Star[/gold]).",
                ["SPEED_POTION.description"] = "Your monsters gain [blue]+500[/blue] DEF until your next turn ([gold]Moon Star[/gold]).",
                ["STRENGTH_POTION.description"] = "Your monsters gain [blue]+500[/blue] ATK until your next turn ([gold]Sun Star[/gold]).",
                ["DEXTERITY_POTION.description"] = "Your monsters gain [blue]+500[/blue] DEF until your next turn ([gold]Moon Star[/gold]).",
                ["FYSH_OIL.description"] = "Your monsters gain [blue]+500[/blue] ATK and [blue]+500[/blue] DEF until your next turn ([gold]Sun Star[/gold] and [gold]Moon Star[/gold]).",
            });
            loc.GetTable("relics").MergeWith(new Dictionary<string, string>
            {
                ["GIRYA.description"] = "You can now Lift at Rest Sites (3 times max). Each Lift gives a [gold]Sun Star[/gold] at the start of each combat: your monsters get [blue]+500[/blue] ATK on the first turn.",
            });
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] guardian star text: " + ex.Message); }
    }

    // ---------------- icons ----------------
    static readonly Dictionary<string, Texture2D?> _icons = new();

    public static Texture2D? Icon(string name)
    {
        if (_icons.TryGetValue(name, out var t)) return t;
        var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmGuardianStars).Assembly.Location) ?? ".", "stars", name + ".png");
        t = System.IO.File.Exists(file) && Image.LoadFromFile(file) is { } img ? ImageTexture.CreateFromImage(img) : null;
        return _icons[name] = t;
    }

    public static Texture2D? RelicIcon(RelicModel r) => RelicIcons.TryGetValue(r.Id.Entry, out var n) ? Icon(n) : null;
    public static Texture2D? PotionIcon(PotionModel p) => PotionIcons.TryGetValue(p.Id.Entry, out var n) ? Icon(n) : null;
}
