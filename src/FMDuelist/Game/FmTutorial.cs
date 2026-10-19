using FMDuelist.Cards;
using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// Before the first battle of Act 1: a popup with the Forbidden Memories duel rules, how fusing works, and a beginner
/// fusion cheat sheet worked out from the disc's fusion table (your deck's best fusions + good Act 1 combos).
/// </summary>
public static class FmTutorial
{
    public static bool Open => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.IsInsideTree();
    static CanvasLayer? _layer;


    /// <summary>Called at the start of each of the player's turns.</summary>
    public static void MaybeShow(Player player)
    {
        try
        {
            if (Open) return;
            var rs = player.RunState;
            if (FmPractice.Active || rs.CurrentActIndex != 0 || player.Creature.CombatState?.RoundNumber != 1) return;
            var act = rs.MapPointHistory.Count > 0 ? rs.MapPointHistory[0] : null;
            int fights = act?.Count(e => e.Rooms.Any(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)) ?? 0;
            if (fights > 1) return;
            Show(player);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] tutorial: " + (ex.InnerException ?? ex)); }
    }

    static readonly Color Gold = new(1f, 0.82f, 0.35f), Ink = new(0.95f, 0.92f, 0.85f);

    public static void Show(Player player, int startTab = 0)
    {
        var game = NGame.Instance;
        if (game == null) return;
        var layer = new CanvasLayer { Layer = 120 };
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.7f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(dim);

        var panel = new PanelContainer();
        panel.AnchorLeft = 0.14f; panel.AnchorRight = 0.86f; panel.AnchorTop = 0.07f; panel.AnchorBottom = 0.93f;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.08f, 0.05f, 0.97f), BorderColor = Gold,
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 18,
        });
        dim.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var title = new Label { Text = "Yu-Gi-Oh! Forbidden Memories Duel Rules", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", Gold);
        box.AddChild(title);

        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        tabs.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(tabs);
        tabs.AddChild(Page("Rules", RulesText()));
        tabs.AddChild(Page("Fusing", FusingText(player)));
        tabs.AddChild(StarsPage());
        tabs.AddChild(Page("Cheat Sheet", SafeCheatSheet(player)));
        tabs.AddChild(Page("Junk Cards", JunkText(player)));
        tabs.AddChild(Page("Bosses", BossText()));
        tabs.CurrentTab = startTab;

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        box.AddChild(buttons);
        buttons.AddChild(MakeButton("Let's Duel!", () => Close(false)));

        game.AddChild(layer);
        _layer = layer;
        Log.Info("[FMDuelist] tutorial shown");
    }

    // ---------------- corner button: opens the guide on the Cheat Sheet ----------------
    static CanvasLayer? _btnLayer;
    static Button? _btn;

    public static void EnsureButton()
    {
        if (_btnLayer != null && GodotObject.IsInstanceValid(_btnLayer)) return;
        var game = NGame.Instance;
        if (game == null) return;
        _btnLayer = new CanvasLayer { Layer = 90 };
        _btn = new Button
        {
            Text = " Fusion Guide", TooltipText = "Forbidden Memories rules and your fusion cheat sheet",
            Icon = FmData.Art(189), ExpandIcon = true, CustomMinimumSize = new Vector2(170, 46), Visible = false,
            FocusMode = Control.FocusModeEnum.None,
        };
        _btn.AddThemeFontSizeOverride("font_size", 18);
        _btn.AddThemeConstantOverride("icon_max_width", 36);
        // top centre, clear of the Life Point box (top right) and the field box (top left)
        _btn.AnchorLeft = _btn.AnchorRight = 0.5f; _btn.AnchorTop = _btn.AnchorBottom = 0;
        _btn.OffsetLeft = 230; _btn.OffsetRight = 400; _btn.OffsetTop = 8; _btn.OffsetBottom = 54;
        _btn.Connect(BaseButton.SignalName.Pressed, Callable.From(() =>
        {
            if (!Open && FmField.Duelist is { } p) Show(p, startTab: 2);
        }));
        _btnLayer.AddChild(_btn);
        game.AddChild(_btnLayer);
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
        {
            if (_btn != null && GodotObject.IsInstanceValid(_btn))
                _btn.Visible = FmRules.RunActive && FmField.Duelist != null && MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance != null && !Open;
        }));
    }

    static Control StarsPage()
    {
        var page = Page("Guardian Stars", FmStars.GuideText());
        if (FmScene.Tex("guardian_chart") is not { } chart || page.GetChildOrNull<RichTextLabel>(0) is not { } text) return page;
        // FM's chart at the top of the page, then the rules
        text.Clear();
        text.AddImage(chart, 640, (int)(640f * chart.GetHeight() / chart.GetWidth()));
        text.AppendText("\n\n" + FmStars.GuideText());
        return page;
    }

    static Control Page(string name, string bbcode)
    {
        var text = new RichTextLabel
        {
            Name = name, BbcodeEnabled = true, ScrollActive = true, FitContent = false,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Fill(text, bbcode);
        text.AddThemeFontSizeOverride("normal_font_size", 21);
        text.AddThemeFontSizeOverride("bold_font_size", 22);
        text.AddThemeColorOverride("default_color", Ink);
        var margin = new MarginContainer { Name = name };
        margin.AddThemeConstantOverride("margin_left", 12); margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10); margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.AddChild(text);
        return margin;
    }

    static readonly System.Text.RegularExpressions.Regex Marker = new(@"\{\{(img:(\d+):(\d+)|marked|port:(\w+):(\d+))\}\}");

    static void Fill(RichTextLabel label, string bbcode)
    {
        int at = 0;
        foreach (System.Text.RegularExpressions.Match m in Marker.Matches(bbcode))
        {
            if (m.Index > at) label.AppendText(bbcode[at..m.Index]);
            at = m.Index + m.Length;
            try
            {
                if (m.Value == "{{marked}}") { if (MarkedExample() is { } ex) label.AddImage(ex, ex.GetWidth(), ex.GetHeight()); continue; }
                if (m.Groups[4].Success)
                {
                    // a base-game card's portrait (junk cards)
                    var type = typeof(MegaCrit.Sts2.Core.Models.Cards.Dazed).Assembly.GetType("MegaCrit.Sts2.Core.Models.Cards." + m.Groups[4].Value);
                    int sz = int.Parse(m.Groups[5].Value);
                    if (type != null && MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(MegaCrit.Sts2.Core.Models.ModelDb.GetId(type)).Portrait is { } pt)
                        label.AddImage(pt, sz * pt.GetWidth() / Math.Max(1, pt.GetHeight()), sz);
                    continue;
                }
                int id = int.Parse(m.Groups[2].Value), size = int.Parse(m.Groups[3].Value);
                if (FmData.Art(id) is { } t) label.AddImage(t, size * 102 / 96, size);
            }
            catch { }
        }
        if (at < bbcode.Length) label.AppendText(bbcode[at..]);
    }

    static string Pic(int id, int size) => $"{{{{img:{id}:{size}}}}}";

    // tiny 3x5 digits for the order badges
    static readonly string[] Digits = { "111101101101111", "010110010010111", "111001111100111" };
    static ImageTexture? _marked;
    static int[] _markedIds = Array.Empty<int>();

    /// <summary>Three hand cards glowing yellow with their chain numbers 1, 2, 3 in the top-right corner.</summary>
    static ImageTexture? MarkedExample()
    {
        if (_marked != null) return _marked;
        const int cw = 102, ch = 96, gap = 26, pad = 8;
        int n = Math.Min(3, _markedIds.Length);
        if (n == 0) return null;
        var img = Image.CreateEmpty(n * (cw + 2 * pad) + (n - 1) * gap, ch + 2 * pad, false, Image.Format.Rgba8);
        var glow = new Color(1f, 0.85f, 0.2f);
        for (int i = 0; i < n; i++)
        {
            var art = FmData.Art(_markedIds[i])?.GetImage();
            if (art == null) continue;
            var a = (Image)art.Duplicate(); a.Convert(Image.Format.Rgba8);
            int ox = i * (cw + 2 * pad + gap);
            for (int y = 0; y < ch + 2 * pad; y++)
                for (int x = 0; x < cw + 2 * pad; x++)
                {
                    int dx = Math.Max(0, Math.Max(pad - x, x - (cw + pad - 1))), dy = Math.Max(0, Math.Max(pad - y, y - (ch + pad - 1)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 0) { if (d <= pad) img.SetPixel(ox + x, y, new Color(glow, 1f - d / pad)); }
                }
            img.BlitRect(a, new Rect2I(0, 0, cw, ch), new Vector2I(ox + pad, pad));
            // number badge: dark circle with a yellow digit
            int bx = ox + pad + cw - 20, by = pad + 4, r = 15;
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r) img.SetPixel(bx + x, by + 12 + y, x * x + y * y >= (r - 2) * (r - 2) ? glow : new Color(0.08f, 0.05f, 0.02f));
            var dg = Digits[i];
            for (int y = 0; y < 5; y++) for (int x = 0; x < 3; x++)
                if (dg[y * 3 + x] == '1')
                    img.FillRect(new Rect2I(bx - 6 + x * 4, by + 12 - 10 + y * 4, 4, 4), glow);
        }
        return _marked = ImageTexture.CreateFromImage(img);
    }

    static Button MakeButton(string label, Action onPress)
    {
        var b = new Button { Text = label, CustomMinimumSize = new Vector2(260, 52) };
        b.AddThemeFontSizeOverride("font_size", 22);
        b.Connect(BaseButton.SignalName.Pressed, Callable.From(onPress));
        return b;
    }

    public static void Close(bool forever = false)
    {
        if (Open) _layer!.QueueFree();
        _layer = null;
    }

    /// <summary>Keys while the popup is open: Enter/Escape close it, everything else is swallowed.</summary>
    public static void HandleKey(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k && (k.Keycode is Key.Enter or Key.KpEnter or Key.Escape or Key.Space)) Close(false);
    }

    const string H = "[color=#ffd15a][b]", HE = "[/b][/color]";

    static string RulesText() => string.Join("\n",
        $"{H}Life Points{HE}",
        "You start with [b]8000 LP[/b]. Enemies have 100x their normal HP and hit 100x as hard.",
        "",
        $"{H}Your hand{HE}",
        "Each turn you draw back up to [b]5 cards[/b]. Cards you don't play stay in your hand. Cards cost nothing to play.",
        "If you end your turn holding more than 5 cards, you choose which ones to discard to get back down to 5.",
        "",
        $"{H}Summoning{HE}",
        "You can summon [b]one monster per turn[/b]. You have [b]3 monster zones[/b]: pick a zone, then pick a position.",
        "",
        $"{H}Attack position{HE}",
        "- Click it and aim at an enemy to attack for its [b]ATK[/b], once per turn. Monsters only attack when you tell them to.",
        "- Once it has attacked, it can't switch to Defense that turn.",
        "- When an enemy hits it for less than its ATK, it isn't destroyed and the enemy takes the difference.",
        "- An equal hit destroys it, and it hits back for its ATK.",
        "- If the hit is bigger than its ATK, there's no hit back and you take the difference.",
        "",
        $"{H}Defense position{HE}",
        "- At the end of your turn you gain [b]Block equal to its DEF[/b].",
        "- If the Block isn't broken, the rest of the hit is reflected back at the attacker.",
        "",
        $"{H}Monsters{HE}",
        "- Monsters have [b]1 HP[/b]. Any enemy attack destroys them.",
        "- While you have a monster on the field, enemies hit the monster instead of you.",
        "- Right-click a monster on the field to switch between Attack and Defense.",
        "- [b]Transform +[/b] (replaces upgrading): the card turns into a random card [b]one rarity higher[/b] (starter -> Common -> Uncommon -> Rare; a Rare becomes another Rare). Monsters stay monsters; spells, traps and equips stay spells, traps and equips.",
        "- [b]Thorns:[/b] when one of your monsters attacks an enemy with Thorns, the thorns hit [b]you directly[/b], not your monster: you lose 100 Life Points per Thorns stack (3 Thorns = 300 LP).",
        "",
        $"{H}Spells, traps and equips{HE}",
        "- You can play as many spells as you like, but only [b]one Raigeki or Dark Hole per turn[/b].",
        "- Dark Hole also destroys your own monsters, and you can't summon again that turn. It can't be played after any of your monsters attacked that turn.",
        "- Raigeki, Dark Hole, Swords of Revealing Light and Field spells are Exhausted when played.",
        "- You can have up to [b]3 traps[/b] set at once.",
        "- Equip cards: drag one onto a monster that can use it (types listed on the card) to give it the bonus while it stays on the field.",
        "",
        $"{H}Between battles{HE}",
        "- Your currency is [b]Star Chips[/b].",
        "- There are no card upgrades. At rest sites, [b]Cut[/b] removes cards from your deck: up to 1 in Act 1, 2 in Act 2 and 3 in Act 3.",
        "",
        $"{H}Exodia Card Box{HE}",
        "- Neow sometimes offers the Exodia Card Box: all five pieces of Exodia the Forbidden One go into your deck. Hold all five in your hand at once to obliterate every enemy.",
        "- Taking it adds a [b]15-card minimum[/b] to the run: your deck is topped up to 15 cards, and Cut and shop card removal can't take it below 15.");

    static string FusingText(Player player)
    {
        string example = "";
        try
        {
            var deck = player.Deck.Cards.OfType<FmMonsterCard>().Select(c => c.FmId).Distinct().ToList();
            var best = Fusions(deck, player, int.MaxValue).Take(2).ToList();
            if (best.Count > 0)
            {
                _markedIds = new[] { best[0].a, best[0].b }.Concat(deck.Where(d => d != best[0].a && d != best[0].b).Take(1)).ToArray();
                _marked = null;
                var lines = new List<string> { "", $"{H}Example from your deck{HE}" };
                foreach (var f in best)
                {
                    lines.Add($"{Pic(f.a, 96)}  [font_size=34]+[/font_size]  {Pic(f.b, 96)}  [font_size=34]=[/font_size]  {Pic(f.r, 96)}");
                    lines.Add($"{FmData.Get(f.a).Name} + {FmData.Get(f.b).Name} = [color=#ffd15a]{FmData.Get(f.r).Name}[/color] ({FmData.Get(f.r).Atk}/{FmData.Get(f.r).Def})");
                    lines.Add("");
                }
                example = string.Join("\n", lines);
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] fusion example: " + ex.Message); }
        return FusingBody(example);
    }

    static string FusingBody(string example) => string.Join("\n",
        $"{H}How to fuse{HE}",
        "1. [b]Right-click[/b] cards in your hand (any card) to add them to a fusion chain. Each one glows yellow and shows its order number.",
        _markedIds.Length > 0 ? "{{marked}}" : "",
        "2. [b]Play one of the marked cards[/b] (drag it out). The chain fuses in number order: card 1 + card 2, then that result + card 3, and so on.",
        "3. If two cards make a fusion, the new monster continues the chain. If they don't, the first card is knocked away and the second one carries on.",
        "4. When the chain is done, choose Attack or Defense for the monster that's left.",
        example,
        "",
        $"{H}Fusing with a monster already on the field{HE}",
        "- Drag a monster card's arrow onto [b]one of your own monsters on the field[/b]. The field monster becomes the [b]first piece[/b] of the chain, followed by any cards you marked.",
        "- Summoning into a zone that already has a monster also fuses with it.",
        "",
        $"{H}Healing fusions{HE}",
        "Mark healing spells with right-click and play one of them: they fuse up the healing ladder (one step above the stronger card), so spare heals never clog your hand.",
        $"{Pic(338, 56)} + {Pic(338, 56)} = {Pic(339, 56)}   Mooyan Curry + Mooyan Curry = [color=#ffd15a]Red Medicine[/color]",
        $"{Pic(338, 56)} + {Pic(339, 56)} = {Pic(340, 56)}   Mooyan Curry or Red Medicine + Red Medicine = [color=#ffd15a]Goblin's Secret Remedy[/color]",
        $"{Pic(339, 56)} + {Pic(340, 56)} = {Pic(341, 56)}   any heal + Goblin's Secret Remedy = [color=#ffd15a]Soul of the Pure[/color]",
        $"{Pic(340, 56)} + {Pic(341, 56)} = {Pic(342, 56)}   any heal + Soul of the Pure = [color=#ffd15a]Dian Keto the Cure Master[/color]",
        "Dian Keto is the top: fusing more heals into it keeps it Dian Keto.",
        "",
        $"{H}Cards that don't fuse{HE}",
        "- You can right-click any card. Spells, traps and rituals that can't fuse are knocked out of the chain (a failed fusion) and leave your hand, which is a handy way to clear cards you don't need.",
        "",
        $"{H}Equips in a chain{HE}",
        "- Mark an equip card after a monster. If the monster can use it, the equip powers up the fused result.",
        "",
        $"{H}Tips{HE}",
        "- Fusions depend on monster types. Two weak monsters can make one much stronger monster.",
        "- Fusing costs cards, but your hand fills back up to 5 every turn, so fuse often.",
        "- Strong Dragon fusions (like Twin-Headed Thunder Dragon) need the [b]Dragon Treasure[/b] starting option.",
        "- Check the Cheat Sheet tab for fusions you can make with your own deck right now.");

    static string BossText() => string.Join("\n",
        $"{H}Boss duels{HE}",
        "Bosses don't fight like normal enemies: they [b]duel[/b] you by Yu-Gi-Oh! Forbidden Memories rules.",
        "",
        $"{H}The three boss duelists{HE}",
        "- [b]Act 1: Young Priest Seto[/b] (the Kin Priest). His deck comes from Priest Seto's card pool in Forbidden Memories (his first duel, before the world tournament).",
        "- [b]Act 2: the \"Final\" Duelist[/b] (the Knowledge Demon). Seto Kaiba's deck from the World Tournament finals.",
        "- [b]Act 3: Final Priest Seto[/b] (the Queen). Priest Seto's deck from his third duel, the one before DarkNite.",
        "",
        $"{H}How they duel{HE}",
        "- Each has [b]8000 Life Points[/b] and [b]3 monster zones[/b] of their own. Their minions are gone and they never use their own attacks: they only duel.",
        "- [b]You go first, so you can't attack on your first turn[/b] (just like a Forbidden Memories duel). Summon, fuse, equip and set traps instead.",
        "- They show 5 face-down cards, but like Forbidden Memories duelists they secretly choose from a hidden pool of up to 20. One summon per turn, and they always make the strongest fusion they can.",
        "- They play cards just like you: heals when hurt, Raigeki or Dark Hole when your field is stronger, burn spells, one summon (their strongest monster or fusion), equips and face-down traps, all in the same turn.",
        "- Their monsters are [b]set face down[/b]: you only see what they are once they battle (fusions are shown as they happen).",
        "- When none of their monsters can beat yours, the ones that haven't attacked switch to Defense Position.",
        "",
        $"{H}Battles (both sides){HE}",
        "- [b]Attack vs Attack[/b]: the higher ATK wins. The losing monster is destroyed and its owner loses the difference in Life Points. A tie destroys both.",
        "- [b]Attack vs Defense[/b]: if ATK is higher than DEF, the defender is destroyed with no Life Point damage. If DEF is higher, the attacker's owner loses the difference.",
        "- You [b]can't attack Seto directly[/b] while he has monsters, and he can't attack you directly while you have monsters.",
        "- [b]Raigeki[/b] destroys all of his monsters, and [b]Dark Hole[/b] destroys all monsters on the field. Neither damages Life Points.",
        "- Your traps can spring on his attacks, and he sets traps of his own. Swords of Revealing Light stops him attacking for 3 turns; Spellbinding Circle makes him skip a turn.",
        "",
        $"{H}Duel tip{HE}",
        "- Watch the gold commentary line at the top of the screen: it calls out every summon, fusion, spell and attack your opponent makes.");

    static string JunkText(Player player)
    {
        int mon = player.Deck.Cards.OfType<FmMonsterCard>().Select(c => c.FmId).FirstOrDefault(2);
        string M(int size) => Pic(mon, size);
        const string P = "[font_size=30]+[/font_size]", E = "[font_size=30]=[/font_size]";
        return string.Join("\n",
            $"{H}Junk cards{HE}",
            "Enemies and events can put junk cards (statuses and curses) in your hand. They take up space, but you can get rid of them by using them as [b]fusion material[/b]: right-click them to mark them like any other card.",
            "Junk never fuses with anything, so it's always a failed fusion.",
            "",
            $"{H}Harmless junk{HE}  (Dazed, Slimed, Wound, Void...)",
            $"{M(80)} {P} {{{{port:Dazed:80}}}} {E} {M(80)}",
            "The junk card is knocked away and your monster carries on with the rest of the chain.",
            "",
            $"{H}Junk that hurts you while it's in your hand{HE}  (Burn, Decay, Doubt, Regret...)",
            $"{M(80)} {P} {{{{port:Burn:80}}}} {E} [font_size=30][color=#ff6a3d]both destroyed[/color][/font_size]",
            "It takes the monster down with it: the monster burns away and the junk card is removed for the rest of the combat. This still uses your summon for the turn, so it's a trade: a monster for a safe hand.",
            "If you fuse onto a monster on the field this way, that monster is destroyed.",
            "",
            $"{H}Step Back{HE}  (Frantic Escape, from the sand worm)",
            $"{{{{port:FranticEscape:80}}}} {P} {{{{port:FranticEscape:80}}}} {P} {{{{port:FranticEscape:80}}}} {E} [font_size=30][color=#ffd15a]Step back x3[/color][/font_size]",
            "Play it like a spell to step back from the worm. It [b]uses your monster summon[/b] for the turn and vanishes once it takes effect.",
            "It only fuses with itself: mark several Step Back cards and play one to step back that many spaces at once (up to 5).");
    }

    static string SafeCheatSheet(Player player)
    {
        try { return CheatSheet(player); }
        catch (Exception ex) { Log.Warn("[FMDuelist] cheat sheet: " + (ex.InnerException ?? ex)); return "The cheat sheet couldn't be built this time."; }
    }

    static string CheatSheet(Player player)
    {
        if (!FmData.Loaded) return "Fusion data isn't loaded.";
        var lines = new List<string>
        {
            $"{H}Type fusions{HE}",
            "Most fusions go by monster type, so any card of one type plus any card of the other works. You get the weakest result that's still stronger than both cards, so stronger cards can make a stronger result.",
            "",
            FmFusionRules.TypeSection(player, Pic),
            "",
            $"{H}Card examples{HE}",
            FmFusionRules.ExampleSection(Pic),
            "",
            $"{H}Fusions you can make with your deck{HE}",
        };
        var deck = player.Deck.Cards.OfType<FmMonsterCard>().Select(c => c.FmId).Distinct().ToList();
        var mine = BestFusions(deck, player, int.MaxValue, maxResults: 12);
        lines.AddRange(mine.Count > 0 ? mine : new List<string> { "No fusions found in your deck yet. Collect more monsters!" });
        lines.Add("");
        lines.Add("[i]Order doesn't matter: A + B makes the same monster as B + A.[/i]");
        return string.Join("\n", lines);
    }

    /// <summary>Distinct-result fusions among the given monsters, best result ATK first.</summary>
    static List<(int a, int b, int r)> Fusions(List<int> ids, Player player, int maxAtk)
    {
        var found = new List<(int a, int b, int r)>();
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                if (FmData.Fuse(ids[i], ids[j]) is not int r) continue;
                var rc = FmData.Get(r);
                if (!rc.IsMonster || !FmFusion.DragonAllowed(r, player)) continue;
                if (rc.Atk <= Math.Max(FmData.Get(ids[i]).Atk, FmData.Get(ids[j]).Atk) || rc.Atk > maxAtk) continue;
                found.Add((ids[i], ids[j], r));
            }
        return found.OrderByDescending(f => FmData.Get(f.r).Atk).ToList();
    }

    static List<string> BestFusions(List<int> ids, Player player, int maxAtk, int maxResults, int perMaterial = 3)
    {
        var found = new List<(int a, int b, int r)>();
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                if (FmData.Fuse(ids[i], ids[j]) is not int r) continue;
                var rc = FmData.Get(r);
                if (!rc.IsMonster || !FmFusion.DragonAllowed(r, player)) continue;
                if (rc.Atk <= Math.Max(FmData.Get(ids[i]).Atk, FmData.Get(ids[j]).Atk)) continue;
                if (rc.Atk > maxAtk) continue;
                found.Add((ids[i], ids[j], r));
            }
        var used = new Dictionary<int, int>();
        var seen = new HashSet<int>();
        var outp = new List<string>();
        foreach (var f in found.OrderByDescending(f => FmData.Get(f.r).Atk))
        {
            if (!seen.Add(f.r)) continue;
            if (used.GetValueOrDefault(f.a) >= perMaterial || used.GetValueOrDefault(f.b) >= perMaterial) continue;
            used[f.a] = used.GetValueOrDefault(f.a) + 1; used[f.b] = used.GetValueOrDefault(f.b) + 1;
            var r = FmData.Get(f.r);
            outp.Add($"{Pic(f.a, 44)} + {Pic(f.b, 44)} = {Pic(f.r, 44)}   {FmData.Get(f.a).Name} + {FmData.Get(f.b).Name} = [color=#ffd15a]{r.Name}[/color] ({r.Atk}/{r.Def})");
            if (outp.Count >= maxResults) break;
        }
        return outp;
    }
}
