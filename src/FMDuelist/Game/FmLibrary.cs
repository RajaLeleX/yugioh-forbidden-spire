using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace FMDuelist.Game;

/// <summary>
/// The Library (replaces the Timeline on the main menu): every Forbidden Memories card from the disc in a searchable,
/// sortable list with full details and artwork, plus a Fusion Calculator that runs any chain of up to 5 cards through
/// the disc's fusion table (in your order, or the best order). Plays FM track 45 while open.
/// </summary>
public static class FmLibrary
{
    public const int MusicTrack = 45;
    static CanvasLayer? _layer;
    public static bool Open => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.IsInsideTree();

    static readonly Color Gold = new(1f, 0.82f, 0.35f), Ink = new(0.94f, 0.91f, 0.84f), Panel = new(0.1f, 0.07f, 0.05f, 0.97f), Dim = new(0.7f, 0.66f, 0.58f);
    static readonly string[] Attributes = { "Light", "Dark", "Earth", "Water", "Fire", "Wind", "Magic", "Trap" };
    static readonly string[] Stars = { "", "Mars", "Jupiter", "Saturn", "Uranus", "Pluto", "Neptune", "Mercury", "Sun", "Moon", "Venus" };

    // ---------------- state ----------------
    static ItemList? _list;
    static LineEdit? _search;
    static OptionButton? _filter, _sort;
    static RichTextLabel? _detail;
    static VBoxContainer? _attack;
    static TextureRect? _art;
    static List<int> _shown = new();
    static int _selected;
    static readonly List<int> _calc = new();
    static HBoxContainer? _calcSlots;
    static RichTextLabel? _calcResult;
    static CheckBox? _bestOrder;
    static ItemList? _calcPicker;
    static LineEdit? _calcSearch;

    public static void Show()
    {
        if (Open || NGame.Instance is not { } game || !FmData.Loaded) return;
        try
        {
            var layer = new CanvasLayer { Layer = 110 };
            var dim = new ColorRect { Color = new Color(0, 0, 0, 0.82f), MouseFilter = Control.MouseFilterEnum.Stop };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(dim);

            var panel = new PanelContainer();
            panel.AnchorLeft = 0.04f; panel.AnchorRight = 0.96f; panel.AnchorTop = 0.05f; panel.AnchorBottom = 0.95f;
            panel.AddThemeStyleboxOverride("panel", Box(Panel, Gold, 3, 12, 20));
            dim.AddChild(panel);
            var root = new VBoxContainer();
            root.AddThemeConstantOverride("separation", 10);
            panel.AddChild(root);

            var header = new HBoxContainer();
            var title = new Label { Text = "Forbidden Memories Library", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            title.AddThemeFontSizeOverride("font_size", 34);
            title.AddThemeColorOverride("font_color", Gold);
            header.AddChild(title);
            header.AddChild(Button("Close", Close, 140));
            root.AddChild(header);

            var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            tabs.AddThemeFontSizeOverride("font_size", 22);
            root.AddChild(tabs);
            tabs.AddChild(CardsTab());
            tabs.AddChild(CalculatorTab());

            game.AddChild(layer);
            _layer = layer;
            // the filter and sort lists open inside the game's window; as separate windows they take its focus and
            // the game mutes itself in the background (the music stopped)
            var gameView = game.GetViewport();
            _embedWas = gameView.GuiEmbedSubwindows;
            gameView.GuiEmbedSubwindows = true;
            Refresh();
            Select(_shown.FirstOrDefault(1));
            FmMusic.Play(MusicTrack);
            Log.Info("[FMDuelist] Library opened");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] library: " + ex); }
    }

    static bool _embedWas;

    public static async void Close()
    {
        if (!Open) return;
        var layer = _layer!;
        _layer = null;
        if (NGame.Instance?.GetViewport() is { } root) root.GuiEmbedSubwindows = _embedWas;
        await FmLibraryChoice.FadeAway(layer);
        FmMusic.Play(FmMedia.MenuTrack);
    }

    /// <summary>Esc closes the Library; everything else goes to its search boxes and buttons.</summary>
    public static bool HandleKey(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { Close(); return true; }
        return false;
    }

    // ---------------- building blocks ----------------
    static StyleBoxFlat Box(Color bg, Color border, int bw, int radius, int margin) => new()
    {
        BgColor = bg, BorderColor = border, BorderWidthLeft = bw, BorderWidthRight = bw, BorderWidthTop = bw, BorderWidthBottom = bw,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * 0.7f, ContentMarginBottom = margin * 0.7f,
    };

    static Button Button(string text, Action onPress, int width = 160)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(width, 44), FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 20);
        b.Connect(BaseButton.SignalName.Pressed, Callable.From(onPress));
        return b;
    }

    static Label Caption(string text, int size = 20, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Dim);
        return l;
    }

    static string TypeName(FmCardInfo c) => FmData.TypeNames[(int)c.Type];
    static bool IsMonster(FmCardInfo c) => c.IsMonster;

    static string Line(FmCardInfo c) => IsMonster(c)
        ? $"#{c.Id:000}  {c.Name}   ·   {TypeName(c)} · {Attr(c)} · Lv {c.Level}   ·   {c.Atk}/{c.Def}"
        : $"#{c.Id:000}  {c.Name}   ·   {TypeName(c)}";

    static string Attr(FmCardInfo c) => c.Attribute >= 0 && c.Attribute < Attributes.Length ? Attributes[c.Attribute] : "?";

    static Texture2D? Icon(int id) => FmData.Art(id);

    // ---------------- Cards tab ----------------
    static Control CardsTab()
    {
        var tab = new HBoxContainer { Name = "Cards" };
        tab.AddThemeConstantOverride("separation", 16);

        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.25f };
        left.AddThemeConstantOverride("separation", 8);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        _search = new LineEdit { PlaceholderText = "Search by name, number, type or text...", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _search.AddThemeFontSizeOverride("font_size", 20);
        _search.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(_ => Refresh()));
        bar.AddChild(_search);
        _filter = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _filter.AddThemeFontSizeOverride("font_size", 18);
        foreach (var f in new[] { "All cards", "Monsters", "Magic", "Equip", "Trap", "Ritual" }.Concat(FmData.TypeNames.Take(20))) _filter.AddItem(f);
        _filter.Connect(OptionButton.SignalName.ItemSelected, Callable.From<long>(_ => Refresh()));
        bar.AddChild(_filter);
        _sort = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _sort.AddThemeFontSizeOverride("font_size", 18);
        foreach (var s in new[] { "Sort: Number", "Sort: Name", "Sort: ATK", "Sort: DEF", "Sort: Level" }) _sort.AddItem(s);
        _sort.Connect(OptionButton.SignalName.ItemSelected, Callable.From<long>(_ => Refresh()));
        bar.AddChild(_sort);
        left.AddChild(bar);

        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedIconSize = new Vector2I(51, 48), IconMode = ItemList.IconModeEnum.Left, FocusMode = Control.FocusModeEnum.None };
        _list.AddThemeFontSizeOverride("font_size", 19);
        _list.AddThemeColorOverride("font_color", Ink);
        _list.AddThemeStyleboxOverride("panel", Box(new Color(0.05f, 0.04f, 0.03f), new Color(0.35f, 0.28f, 0.15f), 1, 6, 6));
        _list.Connect(ItemList.SignalName.ItemSelected, Callable.From<long>(i => { if (i >= 0 && i < _shown.Count) Select(_shown[(int)i]); }));
        left.AddChild(_list);
        tab.AddChild(left);

        var side = new TabContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        side.AddThemeFontSizeOverride("font_size", 20);
        var right = new ScrollContainer { Name = "Card", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var rbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rbox.AddThemeConstantOverride("separation", 10);
        _art = new TextureRect { CustomMinimumSize = new Vector2(306, 288), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        rbox.AddChild(_art);
        rbox.AddChild(Button("Add to Fusion Calculator", () => { if (_selected > 0) AddToCalc(_selected); }, 300));
        _detail = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ScrollActive = false };
        _detail.AddThemeFontSizeOverride("normal_font_size", 19);
        _detail.AddThemeFontSizeOverride("bold_font_size", 21);
        _detail.AddThemeColorOverride("default_color", Ink);
        rbox.AddChild(_detail);
        right.AddChild(rbox);
        side.AddChild(right);
        // the monster attacking a dummy (no damage numbers), sized to the window
        _attack = new VBoxContainer { Name = "Attack", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        side.AddChild(_attack);
        tab.AddChild(side);
        return tab;
    }

    static void Refresh()
    {
        if (_list == null || _search == null || _filter == null || _sort == null) return;
        string q = _search.Text.Trim().ToLowerInvariant();
        int f = _filter.Selected;
        IEnumerable<FmCardInfo> cards = Enumerable.Range(1, FmData.CardCount).Select(FmData.Get).Where(c => c != null && c.Name.Length > 0);
        cards = f switch
        {
            1 => cards.Where(IsMonster),
            2 => cards.Where(c => c.Type == FmType.Magic),
            3 => cards.Where(c => c.Type == FmType.Equip),
            4 => cards.Where(c => c.Type == FmType.Trap),
            5 => cards.Where(c => c.Type == FmType.Ritual),
            >= 6 => cards.Where(c => (int)c.Type == f - 6),
            _ => cards,
        };
        if (q.Length > 0)
            cards = cards.Where(c => c.Name.ToLowerInvariant().Contains(q) || c.Id.ToString() == q.TrimStart('#', '0') || TypeName(c).ToLowerInvariant().Contains(q)
                                  || c.Desc.ToLowerInvariant().Contains(q));
        cards = _sort.Selected switch
        {
            1 => cards.OrderBy(c => c.Name),
            2 => cards.OrderByDescending(c => c.Atk),
            3 => cards.OrderByDescending(c => c.Def),
            4 => cards.OrderByDescending(c => c.Level).ThenByDescending(c => c.Atk),
            _ => cards.OrderBy(c => c.Id),
        };
        _shown = cards.Select(c => c.Id).ToList();
        _list.Clear();
        foreach (var id in _shown) { int i = _list.AddItem(Line(FmData.Get(id)), Icon(id)); _list.SetItemTooltipEnabled(i, false); }
    }

    static void Select(int id)
    {
        if (_detail == null || _art == null || id <= 0) return;
        _selected = id;
        var c = FmData.Get(id);
        _art.Texture = Icon(id);
        var lines = new List<string> { $"[font_size=30][color=#ffd15a][b]{c.Name}[/b][/color][/font_size]", $"Card #{c.Id:000}  ·  {TypeName(c)}" };
        if (IsMonster(c))
        {
            lines.Add($"Attribute: {Attr(c)}   ·   Level {c.Level}   ·   {new string('★', Math.Clamp(c.Level, 0, 12))}");
            lines.Add($"[font_size=26][b]ATK {c.Atk}   /   DEF {c.Def}[/b][/font_size]");
            string g1 = c.GuardianStar1 >= 0 && c.GuardianStar1 < Stars.Length ? Stars[c.GuardianStar1] : "?", g2 = c.GuardianStar2 >= 0 && c.GuardianStar2 < Stars.Length ? Stars[c.GuardianStar2] : "?";
            lines.Add($"Guardian Stars: {g1} / {g2}");
        }
        lines.Add("");
        lines.Add(string.Join(" ", c.Desc.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())));
        if (IsMonster(c))
        {
            // what it fuses into (strongest first)
            var into = new List<(int with, int r)>();
            for (int b = 1; b <= FmData.CardCount; b++)
                if (FmData.Fuse(id, b) is int r && FmData.Get(r).IsMonster) into.Add((b, r));
            if (into.Count > 0)
            {
                lines.Add("");
                lines.Add($"[color=#ffd15a][b]Fuses into ({into.Select(x => x.r).Distinct().Count()} results):[/b][/color]");
                foreach (var g in into.GroupBy(x => x.r).OrderByDescending(g => FmData.Get(g.Key).Atk).Take(12))
                    lines.Add($"• [b]{FmData.Get(g.Key).Name}[/b] ({FmData.Get(g.Key).Atk}/{FmData.Get(g.Key).Def}) with {string.Join(", ", g.Take(3).Select(x => FmData.Get(x.with).Name))}{(g.Count() > 3 ? $" and {g.Count() - 3} more" : "")}");
            }
        }
        if (c.Type == FmType.Equip && FmData.EquipTargets.TryGetValue(id, out var targets))
        {
            lines.Add("");
            lines.Add($"[color=#ffd15a][b]Equips {targets.Count} monsters[/b][/color], e.g. {string.Join(", ", targets.Take(8).Select(t => FmData.Get(t).Name))}");
        }
        _detail.Text = string.Join("\n", lines);
        ShowAttack(id);
    }

    static void ShowAttack(int id)
    {
        if (_attack == null) return;
        foreach (var c in _attack.GetChildren()) c.QueueFree();
        if (!FmMonsterStage.HasScene(id)) { _attack.AddChild(Caption("Equip cards have no animation.", 22)); return; }
        var stage = new FmMonsterStage(id);
        stage.View.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        stage.View.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        stage.View.CustomMinimumSize = new Vector2(360, 220);
        _attack.AddChild(stage.View);
        _attack.AddChild(Caption(FmData.Get(id).IsMonster ? $"{FmData.Get(id).Name} attacks ({FmAttackVfx.For(id).style})"
            : FmData.Get(id).Type == FmType.Trap ? $"Duel Master K springs {FmData.Get(id).Name}" : $"Duel Master K casts {FmData.Get(id).Name}", 18));
    }

    // ---------------- Fusion Calculator tab ----------------
    static Control CalculatorTab()
    {
        var tab = new VBoxContainer { Name = "Fusion Calculator" };
        tab.AddThemeConstantOverride("separation", 10);
        tab.AddChild(Caption("Add up to 5 cards (search below, or use \"Add to Fusion Calculator\" on the Cards tab). They fuse in order like a marked chain in a duel.", 19, Ink));

        _calcSlots = new HBoxContainer();
        _calcSlots.AddThemeConstantOverride("separation", 10);
        tab.AddChild(_calcSlots);

        var opts = new HBoxContainer();
        opts.AddThemeConstantOverride("separation", 16);
        _bestOrder = new CheckBox { Text = "Find the best order", FocusMode = Control.FocusModeEnum.None };
        _bestOrder.AddThemeFontSizeOverride("font_size", 19);
        _bestOrder.Connect(BaseButton.SignalName.Toggled, Callable.From<bool>(_ => Calculate()));
        opts.AddChild(_bestOrder);
        opts.AddChild(Button("Clear", () => { _calc.Clear(); Calculate(); }, 120));
        tab.AddChild(opts);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        var pick = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _calcSearch = new LineEdit { PlaceholderText = "Find a card to add...", ClearButtonEnabled = true };
        _calcSearch.AddThemeFontSizeOverride("font_size", 20);
        _calcSearch.Connect(LineEdit.SignalName.TextChanged, Callable.From<string>(RefreshPicker));
        pick.AddChild(_calcSearch);
        _calcPicker = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedIconSize = new Vector2I(41, 38), FocusMode = Control.FocusModeEnum.None };
        _calcPicker.AddThemeFontSizeOverride("font_size", 18);
        _calcPicker.AddThemeColorOverride("font_color", Ink);
        // only a left click adds a card (the scroll wheel also reports "clicks")
        _calcPicker.Connect(ItemList.SignalName.ItemClicked, Callable.From<long, Vector2, long>((i, _, button) =>
        {
            if (button == (long)MouseButton.Left && _calcPicker.GetItemMetadata((int)i).AsInt32() is int id and > 0) AddToCalc(id);
        }));
        pick.AddChild(_calcPicker);
        body.AddChild(pick);

        var resScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _calcResult = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ScrollActive = false };
        _calcResult.AddThemeFontSizeOverride("normal_font_size", 20);
        _calcResult.AddThemeColorOverride("default_color", Ink);
        resScroll.AddChild(_calcResult);
        body.AddChild(resScroll);
        tab.AddChild(body);

        RefreshPicker("");
        Calculate();
        return tab;
    }

    static void RefreshPicker(string text)
    {
        if (_calcPicker == null) return;
        string q = text.Trim().ToLowerInvariant();
        _calcPicker.Clear();
        var hits = Enumerable.Range(1, FmData.CardCount).Select(FmData.Get)
            .Where(c => c.Name.Length > 0 && (c.IsMonster || c.Type == FmType.Equip) && (q.Length == 0 || c.Name.ToLowerInvariant().Contains(q) || c.Id.ToString() == q))
            .Take(120);
        foreach (var c in hits)
        {
            int i = _calcPicker.AddItem($"#{c.Id:000} {c.Name}" + (c.IsMonster ? $"  ({c.Atk}/{c.Def})" : "  [Equip]"), Icon(c.Id));
            _calcPicker.SetItemTooltipEnabled(i, false);
            _calcPicker.SetItemMetadata(i, c.Id);
        }
    }

    static void AddToCalc(int id)
    {
        if (_calc.Count >= 5) _calc.RemoveAt(0);
        _calc.Add(id);
        Calculate();
    }

    /// <summary>One chain, FM rules: fusions continue, failed fusions knock the earlier card away, equips power up the current monster.</summary>
    static (int result, List<string> steps, int bonus) Run(IList<int> chain)
    {
        var steps = new List<string>();
        int cur = 0, bonus = 0;
        foreach (var id in chain)
        {
            var c = FmData.Get(id);
            if (c.Type == FmType.Equip)
            {
                if (cur == 0) { steps.Add($"{c.Name}: nothing to equip yet, knocked away"); continue; }
                if (FmData.CanEquip(id, cur)) { bonus += 500; steps.Add($"{c.Name} equips {FmData.Get(cur).Name}: +500 ATK/DEF"); }
                else steps.Add($"{c.Name} doesn't fit {FmData.Get(cur).Name}: knocked away");
                continue;
            }
            if (!c.IsMonster) { steps.Add($"{c.Name} doesn't fuse: knocked away"); continue; }
            if (cur == 0) { cur = id; continue; }
            if (FmData.Fuse(cur, id) is int r && FmData.Get(r).IsMonster)
            {
                steps.Add($"{FmData.Get(cur).Name} + {c.Name} = [color=#ffd15a][b]{FmData.Get(r).Name}[/b][/color] ({FmData.Get(r).Atk}/{FmData.Get(r).Def})");
                cur = r; bonus = 0;
            }
            else
            {
                steps.Add($"{FmData.Get(cur).Name} + {c.Name}: [color=#ff7a5c]no fusion[/color], {FmData.Get(cur).Name} is knocked away");
                cur = id; bonus = 0;
            }
        }
        return (cur, steps, bonus);
    }

    static IEnumerable<List<int>> Permutations(List<int> items)
    {
        if (items.Count <= 1) { yield return new List<int>(items); yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, k) => k != i).ToList();
            foreach (var p in Permutations(rest)) { p.Insert(0, items[i]); yield return p; }
        }
    }

    static void Calculate()
    {
        if (_calcSlots == null || _calcResult == null) return;
        foreach (var child in _calcSlots.GetChildren()) child.QueueFree();
        for (int i = 0; i < 5; i++)
        {
            var slot = new VBoxContainer { CustomMinimumSize = new Vector2(130, 0) };
            var frame = new PanelContainer();
            frame.AddThemeStyleboxOverride("panel", Box(new Color(0.05f, 0.04f, 0.03f), i < _calc.Count ? Gold : new Color(0.3f, 0.25f, 0.15f), 2, 6, 4));
            var tex = new TextureRect { CustomMinimumSize = new Vector2(122, 115), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Texture = i < _calc.Count ? Icon(_calc[i]) : null };
            frame.AddChild(tex);
            slot.AddChild(frame);
            var name = Caption(i < _calc.Count ? $"{i + 1}. {FmData.Get(_calc[i]).Name}" : $"{i + 1}. (empty)", 16, i < _calc.Count ? Ink : Dim);
            name.CustomMinimumSize = new Vector2(130, 0);
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            slot.AddChild(name);
            if (i < _calc.Count)
            {
                int idx = i;
                slot.AddChild(Button("Remove", () => { if (idx < _calc.Count) _calc.RemoveAt(idx); Calculate(); }, 120));
            }
            _calcSlots.AddChild(slot);
        }

        if (_calc.Count == 0) { _calcResult.Text = "[color=#c8bca8]Pick cards on the left to see what they fuse into.[/color]"; return; }
        var chain = _calc.ToList();
        if (_bestOrder?.ButtonPressed == true && chain.Count > 1)
            chain = Permutations(chain).OrderByDescending(p => { var (r, _, b) = Run(p); return r == 0 ? -1 : FmData.Get(r).Atk + b; }).First();
        var (result, steps, bonus) = Run(chain);
        var lines = new List<string>();
        if (_bestOrder?.ButtonPressed == true) lines.Add($"[color=#c8bca8]Best order: {string.Join(" → ", chain.Select(c => FmData.Get(c).Name))}[/color]");
        lines.AddRange(steps.Select((s, i) => $"{i + 1}. {s}"));
        if (result != 0)
        {
            var r = FmData.Get(result);
            lines.Add("");
            lines.Add($"[font_size=28][color=#ffd15a][b]Result: {r.Name}[/b][/color][/font_size]");
            lines.Add($"[font_size=24]ATK {r.Atk + bonus} / DEF {r.Def + bonus}[/font_size]   ({TypeName(r)} · {Attr(r)} · Lv {r.Level}){(bonus > 0 ? $"  incl. +{bonus} from equips" : "")}");
        }
        else lines.Add("\nNo monster comes out of this chain.");
        _calcResult.Text = string.Join("\n", lines);
        if (result != 0 && Icon(result) is { } art) { _calcResult.AppendText("\n"); _calcResult.AddImage(art, 204, 192); }
    }
}
