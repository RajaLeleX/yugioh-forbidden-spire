using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// Everything Forbidden Memories counts during a duel, for its "Results of Duel" screen and Duel Skill rank.
/// </summary>
public static class FmDuelStats
{
    public sealed class Side
    {
        public int CardsUsed, Attacks, AtkSum, EffectiveAttacks, Destroyed, Defends, DefSum, DefLosses, DefWins;
        public int AvgAtk => Attacks == 0 ? 0 : AtkSum / Attacks;
        public int AvgDef => Defends == 0 ? 0 : DefSum / Defends;
    }

    public static Side You = new(), Com = new();
    /// <summary>Your plays that Duel Skill counts: fusions you started, equips, pure magic and traps that sprang.</summary>
    public static int Fusions, Equips, Magic, Traps;
    /// <summary>Cards you set face down (your traps; your monsters are always played face up).</summary>
    public static int FaceDown, ComFaceDown;
    public static bool Exodia, Attrition;

    public static void Reset()
    {
        You = new(); Com = new();
        Fusions = Equips = Magic = Traps = FaceDown = ComFaceDown = 0;
        Exodia = Attrition = false;
    }

    /// <summary>One battle: who attacked, the attacker's ATK, the defender's ATK or DEF, and what happened.</summary>
    public static void Battle(bool youAttack, int atk, int defValue, bool defenderLost, bool attackerLost, bool defenderHurt)
    {
        var a = youAttack ? You : Com; var d = youAttack ? Com : You;
        a.Attacks++; a.AtkSum += atk;
        d.Defends++; d.DefSum += defValue;
        bool effective = defenderLost || defenderHurt;
        if (effective) a.EffectiveAttacks++;
        if (defenderLost) a.Destroyed++;
        if (attackerLost) d.Destroyed++;
        if (effective) d.DefLosses++; else d.DefWins++;
    }

    public static void DirectAttack(bool youAttack, int atk)
    {
        var a = youAttack ? You : Com; var d = youAttack ? Com : You;
        a.Attacks++; a.AtkSum += atk; a.EffectiveAttacks++;
        d.DefLosses++;
    }

    // ---------------- Duel Skill (Forbidden Memories' rank table) ----------------
    static int Band(int v, (int max, int pts)[] bands, int over) { foreach (var (max, pts) in bands) if (v <= max) return pts; return over; }

    /// <summary>
    /// Forbidden Memories' hidden Duel Skill score: it starts at 50, every category below moves it up or down, and
    /// the total is held between 0 and 99. 50 and up is POW (S at 90+), below 50 is TEC (S under 10).
    /// </summary>
    public static List<(string name, string value, int points)> Breakdown(int turns, int lpLeft)
    {
        string cond = Exodia ? "EXODIA" : Attrition ? "ATTRITION" : "TOTAL ANNIHILATION";
        return new()
        {
            ("VICTORY CONDITION", cond, Exodia ? 40 : Attrition ? -40 : 2),
            ("TOTAL TURNS", turns.ToString(), Band(turns, new[] { (4, 12), (8, 8), (28, 0), (32, -8) }, -12)),
            ("EFFECTIVE ATTACKS", You.EffectiveAttacks.ToString(), Band(You.EffectiveAttacks, new[] { (1, 4), (3, 2), (9, 0), (19, -2) }, -4)),
            ("DEFENSIVE WINS", You.DefWins.ToString(), Band(You.DefWins, new[] { (1, 0), (5, -10), (9, -20), (14, -30) }, -40)),
            ("FACE-DOWN PLAYS", FaceDown.ToString(), Band(FaceDown, new[] { (0, 0), (10, -2), (20, -4), (30, -6) }, -8)),
            ("FUSIONS INITIATED", Fusions.ToString(), Band(Fusions, new[] { (0, 4), (4, 0), (9, -4), (14, -8) }, -12)),
            ("EQUIP MAGIC ACTIVATED", Equips.ToString(), Band(Equips, new[] { (0, 4), (4, 0), (9, -4), (14, -8) }, -12)),
            ("PURE MAGIC ACTIVATED", Magic.ToString(), Band(Magic, new[] { (0, 2), (3, -4), (6, -8), (9, -12) }, -16)),
            ("TRAPS TRIGGERED", Traps.ToString(), Band(Traps, new[] { (0, 2), (2, -8), (4, -16), (6, -24) }, -32)),
            ("CARDS USED", You.CardsUsed.ToString(), Band(You.CardsUsed, new[] { (8, 15), (12, 12), (32, 0), (36, -5) }, -7)),
            ("REMAINING LP", lpLeft.ToString(), lpLeft < 100 ? -7 : lpLeft < 1000 ? -5 : lpLeft < 7000 ? 0 : lpLeft < 8000 ? 4 : 6),
        };
    }

    public static int SkillPoints(int turns, int lpLeft) => Math.Clamp(50 + Breakdown(turns, lpLeft).Sum(b => b.points), 0, 99);

    /// <summary>POW for high scores, TEC for low ones; the letter is how far from the middle.</summary>
    public static (string kind, string letter, int stars) Rank(int points)
    {
        if (points >= 50)
            return points >= 90 ? ("POW", "S", 5) : points >= 80 ? ("POW", "A", 4) : points >= 70 ? ("POW", "B", 3) : points >= 60 ? ("POW", "C", 2) : ("POW", "D", 1);
        return points >= 40 ? ("TEC", "D", 1) : points >= 30 ? ("TEC", "C", 2) : points >= 20 ? ("TEC", "B", 3) : points >= 10 ? ("TEC", "A", 4) : ("TEC", "S", 5);
    }
}

/// <summary>
/// The end of a duel, like Forbidden Memories: "YOU WIN!" in burning red letters (or "YOU LOSE" in blue and gold),
/// then the two-page "RESULTS OF DUEL" table (cards used, remaining LP, winner, Duel Skill rank and spoils; then the
/// victory condition, turns, and offense/defense statistics). Click to continue.
/// </summary>
public static class FmDuelEnd
{
    static readonly Color Gold = new(1f, 0.85f, 0.25f), LabelBlue = new(0.45f, 0.75f, 1f), Panel = new(0.16f, 0.17f, 0.2f);

    /// <summary>Plays the whole sequence and returns once the player clicks past the results.</summary>
    /// <summary>FM's own music for the end of a duel: the win jingle, the lose jingle, and the Results of Duel theme.</summary>
    public const int WinJingle = 46, LoseJingle = 47, DuelResultsTheme = 57;

    /// <summary>The rank the last duel earned (POW/TEC and its letter).</summary>
    public static string LastKind = "POW", LastLetter = "D";

    public static async Task Show(bool won, Player player, int comLp, string comName, int turns, bool spoils = true)
    {
        FmMusic.PlayOnce(won ? WinJingle : LoseJingle);
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        int youLp = Math.Max(0, player.Creature.CurrentHp);
        int points = FmDuelStats.SkillPoints(turns, youLp);
        var (kind, letter, stars) = FmDuelStats.Rank(points);
        LastKind = kind; LastLetter = letter;
        Log.Info($"[FMDuelist] Duel over: {(won ? "WIN" : "LOSE")} vs {comName}, turns {turns}, skill {points} -> {letter}-{kind}");
        int chips = won && spoils ? stars * 10 : 0;
        try { if (chips > 0) await PlayerCmd.GainGold(chips, player); } catch (Exception ex) { Log.Warn("[FMDuelist] spoils: " + ex.Message); }

        var layer = new CanvasLayer { Layer = 85 };
        tree.Root.AddChild(layer);
        try
        {
            var view = layer.GetViewport().GetVisibleRect().Size;
            var dim = new ColorRect { Color = new Color(0, 0, 0, 0), Size = view, MouseFilter = Control.MouseFilterEnum.Stop };
            layer.AddChild(dim);
            var dt = dim.CreateTween(); dt.TweenProperty(dim, "color:a", 0.55f, 0.4);

            await Banner(layer, view, won);

            // the results screen: FM's Duel Results theme starts exactly as it comes up
            if (FmMusic.HasFileTrack(DuelResultsTheme)) FmMusic.Play(DuelResultsTheme);
            var dt2 = dim.CreateTween(); dt2.TweenProperty(dim, "color:a", 0.88f, 0.3);
            var page1 = Page1(view, won, youLp, comLp, comName, kind, letter, stars, chips);
            var pages = new[] { page1, Page2(view, won, turns), Page3(view), Page4(view, won, turns, youLp, points, kind, letter) };
            foreach (var pg in pages) { layer.AddChild(pg); pg.Visible = pg == page1; }
            page1.Modulate = new Color(1, 1, 1, 0);
            var pt = page1.CreateTween(); pt.TweenProperty(page1, "modulate:a", 1f, 0.3);

            var done = new TaskCompletionSource();
            int page = 0;
            void Flip(int to) { page = (to + pages.Length) % pages.Length; for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == page; FmSfx.Play(FmSfx.Flip); }
            foreach (var pg in pages)
            {
                (pg.GetNode<Button>("Prev")).Pressed += () => Flip(page - 1);
                (pg.GetNode<Button>("Next")).Pressed += () => Flip(page + 1);
                (pg.GetNode<Button>("Continue")).Pressed += () => done.TrySetResult();
            }
            await done.Task;
            var ft = layer.CreateTween();
            foreach (var c in layer.GetChildren().OfType<CanvasItem>()) ft.Parallel().TweenProperty(c, "modulate:a", 0f, 0.25);
            await ft.ToSignal(ft, Tween.SignalName.Finished);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] duel results: " + ex); }
        finally { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }
    }

    // ---------------- "YOU WIN!" / "YOU LOSE" ----------------
    static async Task Banner(CanvasLayer layer, Vector2 view, bool won)
    {
        var root = new Control { Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(root);
        string[] lines = won ? new[] { "YOU", "WIN!" } : new[] { "YOU", "LOSE" };
        float size = view.Y * 0.2f;
        Color fill = won ? new Color(0.95f, 0.15f, 0.05f) : new Color(0.25f, 0.4f, 1f);
        Color edge = won ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.85f, 0.25f);
        FmSfx.Play(won ? FmSfx.Summon : FmSfx.Destroy);
        for (int l = 0; l < lines.Length; l++)
        {
            float y = view.Y * (0.24f + l * 0.26f);
            for (int i = 0; i < lines[l].Length; i++)
            {
                float x = view.X / 2 + (i - (lines[l].Length - 1) / 2f) * size * 0.72f;
                // flame trail (win): stacked ghost copies above the letter that burn away
                if (won)
                    for (int g = 3; g >= 1; g--)
                    {
                        var ghost = Letter(lines[l][i].ToString(), size, new Color(1f, 0.45f + 0.15f * g, 0.1f, 0.35f / g), new Color(1f, 0.7f, 0.1f, 0.3f / g));
                        ghost.Position = new Vector2(x - size * 0.4f, y - g * size * 0.12f - size * 0.6f);
                        root.AddChild(ghost);
                        var gt = ghost.CreateTween().SetLoops(4);
                        gt.TweenProperty(ghost, "position:y", ghost.Position.Y - size * 0.1f, 0.25);
                        gt.TweenProperty(ghost, "position:y", ghost.Position.Y, 0.25);
                    }
                var letter = Letter(lines[l][i].ToString(), size, fill, edge);
                var home = new Vector2(x - size * 0.4f, y - size * 0.6f);
                letter.Position = won ? home - new Vector2(0, view.Y * 0.6f) : home + new Vector2(0, -size * 0.3f);
                letter.Modulate = new Color(1, 1, 1, 0);
                root.AddChild(letter);
                var t = letter.CreateTween();
                t.TweenInterval(0.08 * (i + l * 3));
                t.TweenProperty(letter, "modulate:a", 1f, 0.12);
                t.Parallel().TweenProperty(letter, "position", home, won ? 0.32 : 0.6).SetTrans(won ? Tween.TransitionType.Bounce : Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            }
        }
        await Wait(2.4);
        var ft = root.CreateTween(); ft.TweenProperty(root, "modulate:a", 0f, 0.3);
        await Wait(0.32);
        root.QueueFree();
    }

    static Label Letter(string s, float size, Color fill, Color edge)
    {
        var l = new Label { Text = s, Size = new Vector2(size * 0.8f, size * 1.2f), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", (int)size);
        l.AddThemeColorOverride("font_color", fill);
        l.AddThemeConstantOverride("outline_size", (int)(size * 0.12f));
        l.AddThemeColorOverride("font_outline_color", edge);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        l.AddThemeConstantOverride("shadow_offset_x", 6); l.AddThemeConstantOverride("shadow_offset_y", 8);
        return l;
    }

    // ---------------- the results pages ----------------
    static Control Frame(Vector2 view, out Control box, out float s)
    {
        s = view.Y / 1080f;
        var root = new Control { Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        var w = 1100 * s; var h = 860 * s;
        box = new Control { Position = new Vector2((view.X - w) / 2, (view.Y - h) / 2), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(box);
        var title = Text("RESULTS OF DUEL", 64 * s, new Color(0.9f, 0.15f, 0.1f), new Color(0.15f, 0.05f, 0.02f), 10 * s);
        title.Position = new Vector2(0, 0); title.Size = new Vector2(w, 90 * s); title.HorizontalAlignment = HorizontalAlignment.Center;
        var plate = new Panel { Position = new Vector2(w * 0.2f, 5 * s), Size = new Vector2(w * 0.6f, 82 * s), MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.3f, 0.3f, 0.32f), BorderColor = new Color(0.12f, 0.12f, 0.13f), BorderWidthBottom = (int)(4 * s), BorderWidthTop = (int)(4 * s), BorderWidthLeft = (int)(4 * s), BorderWidthRight = (int)(4 * s) });
        box.AddChild(plate); box.AddChild(title);
        box.AddChild(ArrowButton("Prev", "◀", new Vector2(10 * s, 15 * s), s));
        box.AddChild(ArrowButton("Next", "▶", new Vector2(w - 80 * s, 15 * s), s));
        var cont = new Button { Name = "Continue", Text = "Continue", Position = new Vector2(w / 2 - 130 * s, h - 70 * s), Size = new Vector2(260 * s, 64 * s) };
        cont.AddThemeFontSizeOverride("font_size", (int)(34 * s));
        root.AddChild(cont); cont.Position += box.Position;
        // buttons are found by name from the page root
        foreach (var b in box.GetChildren().OfType<Button>().ToList()) { box.RemoveChild(b); b.Position += box.Position; root.AddChild(b); }
        return root;
    }

    static Button ArrowButton(string name, string glyph, Vector2 pos, float s)
    {
        var b = new Button { Name = name, Text = glyph, Position = pos, Size = new Vector2(70 * s, 60 * s) };
        b.AddThemeFontSizeOverride("font_size", (int)(36 * s));
        b.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.1f));
        return b;
    }

    static Label Text(string s, float size, Color color, Color outline, float outlineSize)
    {
        var l = new Label { Text = s, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", (int)size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeConstantOverride("outline_size", (int)outlineSize);
        l.AddThemeColorOverride("font_outline_color", outline);
        return l;
    }

    /// <summary>A grey bar with a blue label and YOU / COM values, like FM's.</summary>
    static void Row(Control box, float y, float s, string label, string you, string com, bool header = true)
    {
        float w = box.Size.X;
        if (header)
        {
            var hy = Text("YOU", 24 * s, Colors.White, Colors.Black, 4 * s); hy.Position = new Vector2(w * 0.58f, y - 30 * s); box.AddChild(hy);
            var hc = Text("COM", 24 * s, Colors.White, Colors.Black, 4 * s); hc.Position = new Vector2(w * 0.84f, y - 30 * s); box.AddChild(hc);
        }
        var bar = new Panel { Position = new Vector2(0, y), Size = new Vector2(w, 58 * s), MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Panel, BorderColor = new Color(0.6f, 0.6f, 0.62f),
            BorderWidthBottom = (int)(3 * s), BorderWidthTop = (int)(3 * s), BorderWidthLeft = (int)(3 * s), BorderWidthRight = (int)(3 * s),
            CornerRadiusBottomLeft = (int)(8 * s), CornerRadiusBottomRight = (int)(8 * s), CornerRadiusTopLeft = (int)(8 * s), CornerRadiusTopRight = (int)(8 * s),
        });
        box.AddChild(bar);
        var l = Text(label, 32 * s, LabelBlue, new Color(0, 0, 0.2f), 4 * s); l.Position = new Vector2(24 * s, y + 8 * s); box.AddChild(l);
        var a = Text(you, 32 * s, Colors.White, Colors.Black, 4 * s); a.Position = new Vector2(w * 0.58f, y + 8 * s); box.AddChild(a);
        var b = Text(com, 32 * s, Colors.White, Colors.Black, 4 * s); b.Position = new Vector2(w * 0.86f, y + 8 * s); box.AddChild(b);
    }

    static Control Page1(Vector2 view, bool won, int youLp, int comLp, string comName, string kind, string letter, int stars, int chips)
    {
        var root = Frame(view, out var box, out var s);
        var you = FmDuelStats.You; var com = FmDuelStats.Com;
        Row(box, 140 * s, s, "CARDS USED", you.CardsUsed.ToString(), com.CardsUsed.ToString());
        Row(box, 250 * s, s, "REMAINING LP", youLp.ToString(), comLp.ToString());
        var win = Text($"WINNER  ---  {(won ? "YOU" : "COM")}", 30 * s, Colors.White, Colors.Black, 5 * s);
        win.Position = new Vector2(box.Size.X * 0.5f, 330 * s); box.AddChild(win);

        var skill = Text("DUEL SKILL", 56 * s, new Color(0.35f, 0.5f, 1f), new Color(0.9f, 0.9f, 1f), 6 * s);
        skill.Position = new Vector2(20 * s, 390 * s); box.AddChild(skill);
        var spoils = Text("SPOILS", 56 * s, Gold, new Color(0.35f, 0.15f, 0f), 8 * s);
        spoils.Position = new Vector2(20 * s, 540 * s); box.AddChild(spoils);
        if (won)
        {
            var k = Text(kind, 54 * s, kind == "POW" ? new Color(1f, 0.2f, 0.2f) : new Color(0.3f, 1f, 0.5f), Colors.White, 6 * s);
            k.Position = new Vector2(box.Size.X * 0.56f, 385 * s); box.AddChild(k);
            var rk = Text(letter, 120 * s, new Color(1f, 0.95f, 0.1f), new Color(0.2f, 0.2f, 0.2f), 10 * s);
            rk.Position = new Vector2(box.Size.X * 0.74f, 340 * s); box.AddChild(rk);
            var rkw = Text("Rank", 40 * s, Colors.White, Colors.Black, 6 * s); rkw.Position = new Vector2(box.Size.X * 0.74f + 80 * s, 420 * s); box.AddChild(rkw);
            var st = Text(new string('★', stars), 64 * s, new Color(1f, 0.9f, 0.2f), new Color(0.4f, 0.25f, 0f), 6 * s);
            st.Position = new Vector2(box.Size.X * 0.42f, 535 * s); box.AddChild(st);
            var chip = Text($"+{chips} Star Chips", 36 * s, Colors.White, Colors.Black, 5 * s);
            chip.Position = new Vector2(30 * s, 650 * s); box.AddChild(chip);
        }
        else
        {
            var wn = Text($"WINNER   {comName}", 36 * s, Colors.White, Colors.Black, 5 * s);
            wn.Position = new Vector2(30 * s, 650 * s); box.AddChild(wn);
        }
        return root;
    }

    static Control Page2(Vector2 view, bool won, int turns)
    {
        var root = Frame(view, out var box, out var s);
        var you = FmDuelStats.You; var com = FmDuelStats.Com;
        string cond = FmDuelStats.Exodia ? "EXODIA" : FmDuelStats.Attrition ? "ATTRITION" : "TOTAL ANNIHILATION";
        var vc = Text("VICTORY CONDITIONS", 28 * s, Colors.White, Colors.Black, 4 * s); vc.Position = new Vector2(30 * s, 110 * s); box.AddChild(vc);
        var vcv = Text(won ? cond : "-", 30 * s, Colors.White, Colors.Black, 4 * s); vcv.Position = new Vector2(30 * s, 145 * s); box.AddChild(vcv);
        var tn = Text($"TURNS   {turns}", 30 * s, LabelBlue, Colors.Black, 4 * s); tn.Position = new Vector2(box.Size.X * 0.65f, 125 * s); box.AddChild(tn);
        var off = Text("OFFENSE STATISTICS", 26 * s, Gold, Colors.Black, 4 * s); off.Position = new Vector2(30 * s, 200 * s); box.AddChild(off);
        Row(box, 255 * s, s, "AVERAGE ATK FACTOR", you.AvgAtk.ToString(), com.AvgAtk.ToString());
        Row(box, 325 * s, s, "CARD DESTRUCTION", you.Destroyed.ToString(), com.Destroyed.ToString(), false);
        Row(box, 395 * s, s, "EFFECTIVE ATTACKS", you.EffectiveAttacks.ToString(), com.EffectiveAttacks.ToString(), false);
        var def = Text("DEFENSE STATISTICS", 26 * s, Gold, Colors.Black, 4 * s); def.Position = new Vector2(30 * s, 475 * s); box.AddChild(def);
        Row(box, 530 * s, s, "AVERAGE DFD FACTOR", you.AvgDef.ToString(), com.AvgDef.ToString());
        Row(box, 600 * s, s, "DEFENSIVE LOSSES", you.DefLosses.ToString(), com.DefLosses.ToString(), false);
        Row(box, 670 * s, s, "DEFENSIVE WINS", you.DefWins.ToString(), com.DefWins.ToString(), false);
        return root;
    }

    /// <summary>Page 3: the technique statistics (what FM counts against a POW rank).</summary>
    static Control Page3(Vector2 view)
    {
        var root = Frame(view, out var box, out var s);
        var you = FmDuelStats.You;
        var t = Text("TECHNIQUE STATISTICS", 26 * s, Gold, Colors.Black, 4 * s); t.Position = new Vector2(30 * s, 110 * s); box.AddChild(t);
        Row(box, 170 * s, s, "FACE-DOWN PLAYS", FmDuelStats.FaceDown.ToString(), FmDuelStats.ComFaceDown.ToString(), false);
        Row(box, 240 * s, s, "FUSIONS INITIATED", FmDuelStats.Fusions.ToString(), "-", false);
        Row(box, 310 * s, s, "EQUIP MAGIC ACTIVATED", FmDuelStats.Equips.ToString(), "-", false);
        Row(box, 380 * s, s, "PURE MAGIC ACTIVATED", FmDuelStats.Magic.ToString(), "-", false);
        Row(box, 450 * s, s, "TRAPS TRIGGERED", FmDuelStats.Traps.ToString(), "-", false);
        Row(box, 520 * s, s, "CARD DESTRUCTION", you.Destroyed.ToString(), FmDuelStats.Com.Destroyed.ToString(), false);
        var note = Text("Fusions, magic and traps lower a POW rank and raise a TEC rank.", 22 * s, new Color(0.8f, 0.8f, 0.85f), Colors.Black, 3 * s);
        note.Position = new Vector2(30 * s, 620 * s); box.AddChild(note);
        return root;
    }

    /// <summary>Page 4: how the Duel Skill rank was worked out, category by category.</summary>
    static Control Page4(Vector2 view, bool won, int turns, int youLp, int points, string kind, string letter)
    {
        var root = Frame(view, out var box, out var s);
        var t = Text("DUEL SKILL", 26 * s, Gold, Colors.Black, 4 * s); t.Position = new Vector2(30 * s, 100 * s); box.AddChild(t);
        float y = 140 * s;
        var start = Text("STARTING POINTS", 21 * s, Colors.White, Colors.Black, 3 * s); start.Position = new Vector2(30 * s, y); box.AddChild(start);
        var sv = Text("50", 21 * s, LabelBlue, Colors.Black, 3 * s); sv.Position = new Vector2(box.Size.X - 140 * s, y); box.AddChild(sv);
        foreach (var (name, value, pts) in FmDuelStats.Breakdown(turns, youLp))
        {
            y += 40 * s;
            var n = Text(name, 21 * s, Colors.White, Colors.Black, 3 * s); n.Position = new Vector2(30 * s, y); box.AddChild(n);
            var v = Text(value, 21 * s, new Color(0.85f, 0.85f, 0.9f), Colors.Black, 3 * s); v.Position = new Vector2(box.Size.X * 0.55f, y); box.AddChild(v);
            var p = Text(pts > 0 ? $"+{pts}" : pts.ToString(), 21 * s, pts > 0 ? new Color(1f, 0.45f, 0.4f) : pts < 0 ? new Color(0.4f, 1f, 0.55f) : Colors.White, Colors.Black, 3 * s);
            p.Position = new Vector2(box.Size.X - 140 * s, y); box.AddChild(p);
        }
        y += 52 * s;
        var total = Text(won ? $"TOTAL  {points}   -   {letter}-{kind}" : $"TOTAL  {points}", 28 * s, Gold, Colors.Black, 4 * s);
        total.Position = new Vector2(30 * s, y); box.AddChild(total);
        return root;
    }

    static async Task Wait(double sec)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var t = tree.CreateTimer(sec);
        await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout);
    }
}
