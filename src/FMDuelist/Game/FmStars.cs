using FMDuelist.Data;
using FMDuelist.Relics;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' Guardian Stars. Every monster has two; when it's summoned its owner picks one. In a battle,
/// a monster whose star beats the other monster's star gets +500 ATK and DEF for that battle. The stars beat each
/// other in two circles:
///   Sun → Moon → Venus → Mercury → Sun
///   Mars → Jupiter → Saturn → Uranus → Pluto → Neptune → Mars
/// Two Millennium Items deepen it: the Rod makes your advantage +1000, the Necklace makes a duelist at a disadvantage
/// lose 500 more.
/// </summary>
public static class FmStars
{
    public const int Bonus = 500;

    /// <summary>
    /// A font with the planet symbols as plain outlines (not the coloured emoji Windows otherwise substitutes, which
    /// look like gender icons). Use with Plain() for the text-style variant.
    /// </summary>
    public static readonly SystemFont SymbolFont = new() { MultichannelSignedDistanceField = true, MsdfPixelRange = 16, FontNames = new[] { "Segoe UI Symbol", "Cambria Math", "DejaVu Sans", "Arial Unicode MS" }, AllowSystemFallback = false };
    public static string Plain(string sign) => sign.Length == 0 ? sign : sign + "\uFE0E";
    public static readonly string[] Names = { "", "Mars", "Jupiter", "Saturn", "Uranus", "Pluto", "Neptune", "Mercury", "Sun", "Moon", "Venus" };
    public static readonly string[] Signs = { "", "♂", "♃", "♄", "♅", "♇", "♆", "☿", "☉", "☽", "♀" };

    // what each star beats (FM numbering: 1 Mars .. 10 Venus)
    static readonly Dictionary<int, int> BeatsTable = new()
    {
        [8] = 9,   // Sun beats Moon
        [9] = 10,  // Moon beats Venus
        [10] = 7,  // Venus beats Mercury
        [7] = 8,   // Mercury beats Sun
        [1] = 2,   // Mars beats Jupiter
        [2] = 3,   // Jupiter beats Saturn
        [3] = 4,   // Saturn beats Uranus
        [4] = 5,   // Uranus beats Pluto
        [5] = 6,   // Pluto beats Neptune
        [6] = 1,   // Neptune beats Mars
    };

    public static bool Beats(int a, int b) => a > 0 && b > 0 && BeatsTable.TryGetValue(a, out var x) && x == b;
    public static int BeatenBy(int star) => BeatsTable.FirstOrDefault(kv => kv.Value == star).Key;
    public static int Beaten(int star) => BeatsTable.TryGetValue(star, out var b) ? b : 0;

    public static string Name(int s) => s > 0 && s < Names.Length ? Names[s] : "-";
    public static string Sign(int s) => s > 0 && s < Signs.Length ? Signs[s] : "";

    static (int, int) Pair(int monsterId) { var c = FmData.Get(monsterId); return (c.GuardianStar1, c.GuardianStar2); }

    /// <summary>
    /// The star bonuses for one battle, as (attacker, defender) changes to their ATK/DEF. The winner of the star match
    /// gets +500 (your monsters +1000 with the Millennium Rod); a duelist's monster that loses it also drops 500
    /// with your Millennium Necklace.
    /// </summary>
    public static (int att, int def, string? note) Battle(FmSlot att, FmSlot def, Player player)
    {
        int Up(FmSlot s) => !s.Rival && player.GetRelic<FmItemRod>() != null ? Bonus * 2 : Bonus;
        int Down(FmSlot s) => s.Rival && player.GetRelic<FmItemNecklace>() != null ? -Bonus : 0;
        if (Beats(att.Star, def.Star))
            return (Up(att), Down(def), $"{Name(att.Star)} beats {Name(def.Star)}!");
        if (Beats(def.Star, att.Star))
            return (Down(att), Up(def), $"{Name(def.Star)} beats {Name(att.Star)}!");
        return (0, 0, null);
    }

    /// <summary>His pick: the star that beats the most of your monsters' stars (otherwise the first).</summary>
    public static int ForRival(int monsterId, IEnumerable<FmSlot> yours)
    {
        var (s1, s2) = Pair(monsterId);
        var mine = yours.Where(m => !m.FaceDown).Select(m => m.Star).ToList();
        int Score(int s) => mine.Count(x => Beats(s, x)) - mine.Count(x => Beats(x, s));
        return s2 > 0 && Score(s2) > Score(s1) ? s2 : s1;
    }

    // ---------------- your choice ----------------
    /// <summary>FM's "choose a Guardian Star" after a summon: the monster's two stars; hover says what each beats.</summary>
    public static async Task<int> Choose(int monsterId)
    {
        var (s1, s2) = Pair(monsterId);
        if (s1 <= 0) return s2;
        if (s2 <= 0 || s2 == s1) return s1;
        if (NGame.Instance is not { } game || Engine.GetMainLoop() is not SceneTree tree) return s1;
        var layer = new CanvasLayer { Layer = 95 };
        game.AddChild(layer);
        int picked = 0;
        try
        {
            var view = layer.GetViewport().GetVisibleRect().Size;
            var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), Size = view, MouseFilter = Control.MouseFilterEnum.Stop };
            layer.AddChild(dim);
            float px = view.Y / 240f;
            string title = "CHOOSE GUARDIAN STAR";
            var t = FmTextBox.Text(title, px * 1.1f, new Color(1f, 0.86f, 0.35f));
            t.Position = new Vector2((view.X - FmTextBox.TextWidth(title, px * 1.1f)) / 2, view.Y * 0.2f);
            layer.AddChild(t);
            var name = FmTextBox.Text(FmData.Get(monsterId).Name, px * 0.9f);
            name.Position = new Vector2((view.X - FmTextBox.TextWidth(FmData.Get(monsterId).Name, px * 0.9f)) / 2, view.Y * 0.2f + 16 * px);
            layer.AddChild(name);
            var info = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(info);
            void Explain(int s)
            {
                foreach (var c in info.GetChildren()) c.QueueFree();
                string line = $"{Name(s)} beats {Name(Beaten(s))} and loses to {Name(BeatenBy(s))}.";
                var l = FmTextBox.Text(line, px * 0.75f, new Color(0.85f, 0.9f, 1f));
                l.Position = new Vector2((view.X - FmTextBox.TextWidth(line, px * 0.75f)) / 2, view.Y * 0.72f);
                info.AddChild(l);
            }
            int i = 0;
            foreach (var s in new[] { s1, s2 })
            {
                int star = s;
                float w = 64 * px, h = 64 * px;
                var box = new Panel { Position = new Vector2(view.X / 2 + (i == 0 ? -w - 12 * px : 12 * px), view.Y * 0.36f), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
                var idle = new StyleBoxFlat { BgColor = new Color(0.06f, 0.08f, 0.24f, 0.97f), BorderColor = new Color(0.62f, 0.6f, 0.55f), BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4 };
                var lit = new StyleBoxFlat { BgColor = new Color(0.12f, 0.14f, 0.38f, 0.97f), BorderColor = new Color(0.86f, 0.9f, 0.1f), BorderWidthLeft = 6, BorderWidthRight = 6, BorderWidthTop = 6, BorderWidthBottom = 6 };
                box.AddThemeStyleboxOverride("panel", idle);
                layer.AddChild(box);
                var sign = new Label { Text = Plain(Sign(star)), HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(w, h * 0.7f), MouseFilter = Control.MouseFilterEnum.Ignore };
                sign.AddThemeFontOverride("font", SymbolFont);
                sign.AddThemeFontSizeOverride("font_size", (int)(h * 0.5f));
                sign.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
                box.AddChild(sign);
                var n = FmTextBox.Text(Name(star), px * 0.9f);
                n.Position = new Vector2((w - FmTextBox.TextWidth(Name(star), px * 0.9f)) / 2, h * 0.74f);
                box.AddChild(n);
                var hit = new Button { Flat = true, Position = box.Position, Size = box.Size, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                foreach (var st in new[] { "normal", "hover", "pressed" }) hit.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
                hit.MouseEntered += () => { box.AddThemeStyleboxOverride("panel", lit); Explain(star); FmSfx.Play(FmSfx.Flip); };
                hit.MouseExited += () => box.AddThemeStyleboxOverride("panel", idle);
                hit.Pressed += () => picked = star;
                layer.AddChild(hit);
                i++;
            }
            Explain(s1);
            while (picked == 0 && GodotObject.IsInstanceValid(layer)) await layer.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            FmSfx.Play(FmSfx.Mark);
        }
        finally { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }
        return picked == 0 ? s1 : picked;
    }

    /// <summary>The Fusion Guide's Guardian Star page (with its chart).</summary>
    public static string GuideText() =>
        "[font_size=30][color=#ffd15a][b]Guardian Stars[/b][/color][/font_size]\n\n" +
        "Every monster has two Guardian Stars. When you summon a monster you choose one of them (the duelists choose too). " +
        "In a battle, if your monster's star [b]beats[/b] the other monster's star, your monster gets [color=#7fff7f][b]+500 ATK and DEF[/b][/color] " +
        "for that battle. If their star beats yours, theirs gets it instead. Otherwise nothing changes.\n\n" +
        "[font_size=24][color=#ffd15a][b]The two circles[/b][/color][/font_size]  (each star beats the next one)\n\n" +
        "[font_size=26][b]Sun  →  Moon  →  Venus  →  Mercury  →  Sun[/b][/font_size]\n\n" +
        "[font_size=26][b]Mars  →  Jupiter  →  Saturn  →  Uranus  →  Pluto  →  Neptune  →  Mars[/b][/font_size]\n\n" +
        "[table=3][cell][b]Star[/b]   [/cell][cell][b]Beats[/b]   [/cell][cell][b]Loses to[/b][/cell]" +
        string.Concat(Enumerable.Range(1, 10).Select(s => $"[cell]{Name(s)}   [/cell][cell]{Name(Beaten(s))}   [/cell][cell]{Name(BeatenBy(s))}[/cell]")) +
        "[/table]\n\n" +
        "Stars from different circles (e.g. Sun against Mars) don't affect each other.\n\n" +
        "[color=#ffd15a][b]Millennium Items[/b][/color]\n" +
        "• [b]Millennium Rod[/b]: when your star wins, your monster gets [b]+1000[/b] instead of +500.\n" +
        "• [b]Millennium Necklace[/b]: when a duelist's star loses, their monster also [b]loses 500[/b] ATK and DEF for that battle.\n\n" +
        "A face-down monster's star stays hidden from the duelist until it flips face up.";
}
