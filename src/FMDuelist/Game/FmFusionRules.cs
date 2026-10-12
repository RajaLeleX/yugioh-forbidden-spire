using FMDuelist.Cards;
using FMDuelist.Data;
using MegaCrit.Sts2.Core.Entities.Players;

namespace FMDuelist.Game;

/// <summary>
/// Type fusions ("any Beast + any Pyro = Flame Cerebrus") worked out from the disc's fusion table, plus card-by-card
/// example chains (Twin-Headed Thunder Dragon and friends) for the tutorial's Cheat Sheet.
/// </summary>
public static class FmFusionRules
{
    public sealed record Rule(string Left, string Right, List<int> Results, int Partners, (int a, int b) Example);

    static List<Rule>? _rules;

    public static string TypeName(FmType t) => t switch
    {
        FmType.BeastWarrior => "Beast-Warrior", FmType.WingedBeast => "Winged Beast", FmType.SeaSerpent => "Sea Serpent", _ => t.ToString(),
    };

    static bool Monster(int id) => FmData.Get(id).IsMonster;

    /// <summary>Partners that make the same result with at least half the cards of a type become a rule.</summary>
    public static List<Rule> Rules()
    {
        if (_rules != null) return _rules;
        var mons = Enumerable.Range(1, FmData.CardCount).Where(Monster).ToList();
        var byType = mons.GroupBy(id => FmData.Get(id).Type).ToDictionary(g => g.Key, g => g.ToList());
        var found = new Dictionary<(string, string), Rule>();
        foreach (var (type, cards) in byType)
        {
            if (cards.Count < 4) continue;
            var counts = new Dictionary<int, Dictionary<int, int>>(); // result -> partner -> how many cards of this type
            foreach (var a in cards)
                foreach (var b in mons)
                {
                    if (b == a || FmData.Fuse(a, b) is not int r || !Monster(r)) continue;
                    if (!counts.TryGetValue(r, out var p)) counts[r] = p = new();
                    p[b] = p.GetValueOrDefault(b) + 1;
                }
            foreach (var (r, partners) in counts)
            {
                var set = partners.Where(kv => kv.Value * 2 >= cards.Count).Select(kv => kv.Key).ToList();
                if (set.Count < 5) continue;
                string left = TypeName(type), right = Label(set);
                var key = string.CompareOrdinal(left, right) <= 0 ? (left, right) : (right, left);
                // a cheap example: weakest card of the type + weakest partner that really make it
                var ex = (from a in cards from b in set where a != b && FmData.Fuse(a, b) == r
                          orderby FmData.Get(a).Atk + FmData.Get(b).Atk select (a, b)).FirstOrDefault();
                if (ex == default) continue;
                if (found.TryGetValue(key, out var old))
                {
                    if (!old.Results.Contains(r)) old.Results.Add(r);
                    found[key] = old with { Partners = Math.Max(old.Partners, set.Count) };
                }
                else found[key] = new Rule(left, right, new List<int> { r }, set.Count, ex);
            }
        }
        foreach (var rule in found.Values) rule.Results.Sort((x, y) => FmData.Get(x).Atk.CompareTo(FmData.Get(y).Atk));
        return _rules = found.Values.OrderByDescending(r => r.Partners).ToList();
    }

    /// <summary>A partner group named by its type, as "female" for the mixed group that includes Mystical Elf / Harpie Lady, else by examples.</summary>
    static string Label(List<int> set)
    {
        var top = set.GroupBy(b => FmData.Get(b).Type).OrderByDescending(g => g.Count()).First();
        if (top.Count() * 10 >= set.Count * 6) return TypeName(top.Key);
        if (set.Contains(2) || set.Contains(62)) return "female";
        return "monsters like " + string.Join(" or ", set.Take(2).Select(b => FmData.Get(b).Name));
    }

    static string Any(string label) => label == "female" ? "any female monster" : label.StartsWith("monsters like") ? label : "any " + label;

    public static string TypeSection(Player player, Func<int, int, string> pic, int max = 18)
    {
        var lines = new List<string>();
        foreach (var rule in Rules().Take(max))
        {
            var names = rule.Results.Select(r => $"[color=#ffd15a]{FmData.Get(r).Name}[/color] ({FmData.Get(r).Atk})").ToList();
            string result = names.Count == 1 ? names[0] : names[0] + ", or " + string.Join(" / ", names.Skip(1)) + " with stronger cards";
            string dragonNote = rule.Results.Any(r => !FmFusion.DragonAllowed(r, player)) ? " [i](strong Dragons need the Dragon Treasure)[/i]" : "";
            var (a, b) = rule.Example;
            lines.Add($"{pic(rule.Results[0], 40)}  {Cap(Any(rule.Left))} + {Any(rule.Right)} = {result}{dragonNote}");
            lines.Add($"      [i][color=#c8bca8]e.g. {FmData.Get(a).Name} + {FmData.Get(b).Name}[/color][/i]");
        }
        return string.Join("\n", lines);
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    static int Find(string name) => Enumerable.Range(1, FmData.CardCount).FirstOrDefault(i => FmData.Get(i).Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A material that fuses with <paramref name="with"/> into <paramref name="result"/>, weakest first (optionally of a preferred card).</summary>
    static int Partner(int with, int result, int prefer = 0)
    {
        if (prefer != 0 && FmData.Fuse(with, prefer) == result) return prefer;
        return Enumerable.Range(1, FmData.CardCount).Where(b => Monster(b) && FmData.Fuse(with, b) == result)
            .OrderBy(b => FmData.Get(b).Level).ThenBy(b => FmData.Get(b).Atk).FirstOrDefault();
    }

    /// <summary>Card-by-card chains, drawn with the card art.</summary>
    public static string ExampleSection(Func<int, int, string> pic)
    {
        var lines = new List<string>();
        string Step(int a, int b, int r) =>
            $"{pic(a, 64)} [font_size=30]+[/font_size] {pic(b, 64)} [font_size=30]=[/font_size] {pic(r, 64)}   {FmData.Get(a).Name} + {FmData.Get(b).Name} = [color=#ffd15a]{FmData.Get(r).Name}[/color] ({FmData.Get(r).Atk}/{FmData.Get(r).Def})";

        // Twin-Headed Thunder Dragon: a small Dragon + a Thunder make Thunder Dragon, then Thunder Dragon + a Dragon
        int thd = Find("Twin-headed Thunder Dragon"), td = Find("Thunder Dragon"), baby = Find("Baby Dragon");
        int thunder = baby != 0 && td != 0 ? Partner(baby, td, Find("Kaminarikozou")) : 0;
        int second = td != 0 && thd != 0 ? Partner(td, thd, baby) : 0;
        if (thunder != 0 && second != 0)
        {
            lines.Add($"[b]{FmData.Get(thd).Name}[/b] (3-card chain: mark them 1, 2, 3)");
            lines.Add(Step(baby, thunder, td));
            lines.Add(Step(td, second, thd));
            lines.Add("");
        }
        // a few more classics, built from the type rules
        foreach (var target in new[] { "Flame Cerebrus", "Mystical Sand", "Crimson Sunbird", "B. Dragon Jungle King", "Flame Swordsman" })
        {
            int r = Find(target);
            var rule = Rules().FirstOrDefault(x => x.Results.Contains(r));
            if (r == 0 || rule == null) continue;
            var (a, b) = FmData.Fuse(rule.Example.a, rule.Example.b) == r ? rule.Example
                : (from x in Enumerable.Range(1, FmData.CardCount) where Monster(x) && FmData.Get(x).Level <= 4
                   let y = Partner(x, r) where y != 0 select (x, y)).FirstOrDefault();
            if (a == 0) continue;
            lines.Add(Step(a, b, r));
        }
        return string.Join("\n", lines);
    }
}
