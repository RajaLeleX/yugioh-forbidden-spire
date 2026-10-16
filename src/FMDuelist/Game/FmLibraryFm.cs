using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace FMDuelist.Game;

/// <summary>
/// The Library as Forbidden Memories has it, on FM's 4:3 screen:
///   the table: "&lt; n/722 &gt;" over two stone panels of 10×10 cells (001–100 and 101–200, then 201–400 and so on),
///     a green crosshair on the cell under the mouse, and the card's number and name in the stone bar below;
///   the card: the card itself on the left, its type, Guardian Stars and text in FM's blue boxes on the right;
///   the model: the card turned on its side at the left, and the monster in FM's black-and-white wireframe room
///     attacking a dummy.
/// Click to go in, right click or Esc to go back, the mouse wheel (or the arrow keys) to page or step through cards.
/// </summary>
public static class FmLibraryFm
{
    enum Page { Table, Card, Model }

    static CanvasLayer? _layer;
    static Control? _stage, _screen;
    static float _px, _sw, _sh;
    static Page _page;
    static int _row, _cursor = 1;
    static Control? _crossH, _crossV, _glass;
    static Control? _barNumber, _barName;

    const int Panels = 4; // rows of two panels: 1-200, 201-400, 401-600, 601-722
    static readonly Color Navy = new(0.11f, 0.13f, 0.32f, 0.97f), StoneEdge = new(0.62f, 0.6f, 0.55f), Stone = new(0.21f, 0.21f, 0.2f);
    static readonly Color Lime = new(0.25f, 0.85f, 0.2f);
    static readonly string[] StarNames = { "", "Mars", "Jupiter", "Saturn", "Uranus", "Pluto", "Neptune", "Mercury", "Sun", "Moon", "Venus" };
    static readonly string[] StarSigns = { "", "♂", "♃", "♄", "♅", "♇", "♆", "☿", "☉", "☽", "♀" };

    public static bool Open => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.IsInsideTree();
    static int Count => FmData.CardCount;

    public static void Show()
    {
        if (Open || NGame.Instance is not { } game || !FmData.Loaded) return;
        try
        {
            _layer = new CanvasLayer { Layer = 110 };
            game.AddChild(_layer);
            var view = _layer.GetViewport().GetVisibleRect().Size;
            var black = new ColorRect { Color = Colors.Black, Size = view, MouseFilter = Control.MouseFilterEnum.Stop };
            black.GuiInput += OnInput;
            _layer.AddChild(black);
            _sw = Math.Min(view.X, view.Y * 4f / 3f); _sh = _sw * 3f / 4f;
            _px = _sh / 240f;
            _stage = new Control { Position = new Vector2((view.X - _sw) / 2, (view.Y - _sh) / 2), Size = new Vector2(_sw, _sh), MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
            _layer.AddChild(_stage);
            _page = Page.Table;
            _row = Math.Clamp((_cursor - 1) / 200, 0, Panels - 1);
            Render();
            FmMusic.Play(FmLibrary.MusicTrack);
            Log.Info("[FMDuelist] FM Library opened");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] FM library: " + ex); }
    }

    public static async void Close()
    {
        if (!Open) return;
        var layer = _layer!;
        _layer = null;
        await FmLibraryChoice.FadeAway(layer);
        FmMusic.Play(FmMedia.MenuTrack);
    }

    // ---------------- input ----------------
    /// <summary>Keys and the wheel (from the game's input hook): Esc goes back, arrows and the wheel move.</summary>
    public static bool HandleInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } m)
        {
            switch (m.ButtonIndex)
            {
                case MouseButton.Right: Back(); return true;
                case MouseButton.WheelUp: if (_page == Page.Table) Turn(-1); else Step(-1); return true;
                case MouseButton.WheelDown: if (_page == Page.Table) Turn(1); else Step(1); return true;
            }
            return false;
        }
        if (e is InputEventKey { Pressed: true } k)
        {
            switch (k.Keycode)
            {
                case Key.Escape or Key.Backspace: Back(); return true;
                case Key.Enter or Key.KpEnter or Key.Space: Forward(); return true;
                case Key.Left: Step(-1); return true;
                case Key.Right: Step(1); return true;
                case Key.Up: Step(_page == Page.Table ? -10 : -1); return true;
                case Key.Down: Step(_page == Page.Table ? 10 : 1); return true;
                case Key.Pageup: Turn(-1); return true;
                case Key.Pagedown: Turn(1); return true;
            }
        }
        return false;
    }

    static void OnInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } m) return;
        switch (m.ButtonIndex)
        {
            case MouseButton.Left: if (_page != Page.Table) Forward(); break;
        }
    }

    static void Back()
    {
        FmSfx.Play(FmSfx.Flip);
        switch (_page)
        {
            case Page.Model: _page = Page.Card; Render(); break;
            case Page.Card: _page = Page.Table; _row = (_cursor - 1) / 200; Render(); break;
            default: Close(); break;
        }
    }

    static void Forward()
    {
        FmSfx.Play(FmSfx.Mark);
        switch (_page)
        {
            case Page.Table: _page = Page.Card; Render(); break;
            case Page.Card: if (FmData.Get(_cursor).IsMonster) { _page = Page.Model; Render(); } break;
            default: _page = Page.Card; Render(); break;
        }
    }

    static void Step(int d)
    {
        int next = Math.Clamp(_cursor + d, 1, Count);
        if (next == _cursor) return;
        _cursor = next;
        FmSfx.Play(FmSfx.Flip);
        if (_page == Page.Table)
        {
            if ((_cursor - 1) / 200 != _row) { _row = (_cursor - 1) / 200; Render(); }
            else Hover(_cursor);
        }
        else Render();
    }

    /// <summary>The table scrolls a row of panels (200 cards) at a time.</summary>
    static void Turn(int d)
    {
        int next = Math.Clamp(_row + d, 0, Panels - 1);
        if (next == _row) return;
        FmSfx.Play(FmSfx.Flip);
        _row = next;
        _cursor = Math.Clamp(_row * 200 + 1 + (_cursor - 1) % 200, 1, Count);
        Render();
    }

    // ---------------- drawing ----------------
    static Vector2 P(float x, float y) => new(x * _px, y * _px);

    static Control Box(float x, float y, float w, float h, Color fill, Color edge, int border = 3)
    {
        var p = new Panel { Position = P(x, y), Size = P(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        int b = Math.Max(1, (int)(border * _px / 3));
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = fill, BorderColor = edge, BorderWidthLeft = b, BorderWidthRight = b, BorderWidthTop = b, BorderWidthBottom = b,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = (int)_px,
        });
        _screen!.AddChild(p);
        return p;
    }

    static Control Text(string s, float x, float y, float scale = 1f, Color? tint = null, Control? parent = null)
    {
        var t = FmTextBox.Text(s, _px * scale, tint);
        t.Position = P(x, y);
        (parent ?? _screen!).AddChild(t);
        return t;
    }

    static void Render()
    {
        if (_stage == null) return;
        if (_screen != null && GodotObject.IsInstanceValid(_screen)) _screen.QueueFree();
        _screen = new Control { Size = _stage.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        _stage.AddChild(_screen);
        _crossH = _crossV = _glass = null;
        switch (_page)
        {
            case Page.Table: Table(); break;
            case Page.Card: CardPage(); break;
            default: ModelPage(); break;
        }
        Bar(_page != Page.Table, _page == Page.Model);
        if (_page == Page.Table) Hover(_cursor);
    }

    /// <summary>FM's stone bar along the bottom: the card's number on a black tab, its name below.</summary>
    static void Bar(bool showName, bool flag)
    {
        var bar = new Control { Position = P(0, 203), Size = P(320, 37), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (FmScene.Tex("textbox") is { } tb)
            bar.AddChild(new TextureRect { Texture = tb, Size = P(320, 37), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore });
        else
        {
            var p = new Panel { Size = P(320, 37), MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f), BorderColor = StoneEdge, BorderWidthTop = (int)(4 * _px), BorderWidthLeft = (int)(3 * _px), BorderWidthRight = (int)(3 * _px) });
            bar.AddChild(p);
        }
        bar.AddChild(new ColorRect { Position = P(5, 3), Size = P(50, 11), Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore });
        _screen!.AddChild(bar);
        _barNumber = Text("", 10, 4, 1f, null, bar);
        _barName = Text("", 14, 22, 1.15f, null, bar);
        string label = _page == Page.Table ? "EXIT" : "BACK";
        var back = new Button { Flat = true, Position = P(246, 1), Size = P(48, 15), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        var idle = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = StoneEdge, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 };
        var lit = new StyleBoxFlat { BgColor = new Color(0.86f, 0.9f, 0.1f, 0.9f), BorderColor = StoneEdge, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 };
        back.AddThemeStyleboxOverride("normal", idle); back.AddThemeStyleboxOverride("hover", lit); back.AddThemeStyleboxOverride("pressed", lit);
        var word = FmTextBox.Text(label, _px);
        word.Position = P((48 - label.Length * 8) / 2f, 2);
        back.AddChild(word);
        back.Pressed += Back;
        bar.AddChild(back);
        if (flag) bar.AddChild(new Polygon2D { Color = new Color(0.85f, 0.1f, 0.08f), Polygon = new[] { P(298, 6), P(312, 2), P(312, 12) } });
        SetBar(_cursor, showName);
    }

    static void SetBar(int id, bool showName)
    {
        if (_barNumber?.GetParent() is not Control bar) return;
        _barNumber.QueueFree(); _barName?.QueueFree();
        _barNumber = Text($"{id:000}", 10, 4, 1f, null, bar);
        _barName = Text(showName || _page == Page.Table ? FmData.Get(id).Name : "", 14, 22, 1.15f, null, bar);
    }

    // ---------------- the table ----------------
    static void Table()
    {
        // "< 722/722 >": every card is in the Library; the arrows turn the table
        string head = $"<  {Count}/{Count}>";
        var h = Text(head, (320 - FmTextBox.TextWidth(head, 2f) / _px) / 2, 14, 2f);
        var up = new Button { Flat = true, Position = P(80, 10), Size = P(30, 30), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        var down = new Button { Flat = true, Position = P(210, 10), Size = P(30, 30), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        foreach (var b in new[] { up, down }) { b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); b.AddThemeStyleboxOverride("hover", new StyleBoxEmpty()); b.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty()); _screen!.AddChild(b); }
        up.Pressed += () => Turn(-1);
        down.Pressed += () => Turn(1);
        Panel(_row * 200 + 1, 3);
        Panel(_row * 200 + 101, 162);
        // FM's crosshair: thin green lines through the cell under the cursor, and a magnifying glass
        _crossH = new ColorRect { Color = new Color(Lime, 0.9f), Size = new Vector2(_sw, Math.Max(1, _px * 0.7f)), MouseFilter = Control.MouseFilterEnum.Ignore };
        _crossV = new ColorRect { Color = new Color(Lime, 0.9f), Size = new Vector2(Math.Max(1, _px * 0.7f), P(0, 203).Y), MouseFilter = Control.MouseFilterEnum.Ignore };
        _screen!.AddChild(_crossH); _screen.AddChild(_crossV);
        var glass = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        glass.AddChild(new Line2D { Points = Circle(3.2f * _px), Closed = true, Width = 1.2f * _px, DefaultColor = new Color(0.75f, 0.85f, 1f) });
        glass.AddChild(new Polygon2D { Polygon = Circle(2.6f * _px), Color = new Color(0.4f, 0.55f, 0.95f, 0.55f) });
        glass.AddChild(new Line2D { Points = new[] { P(2.3f, 2.3f), P(5.5f, 5.5f) }, Width = 1.6f * _px, DefaultColor = new Color(0.8f, 0.8f, 0.85f) });
        _screen.AddChild(glass);
        _glass = glass;
    }

    static Vector2[] Circle(float r, int n = 18)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return p;
    }

    const float PanelY = 46, PanelW = 155, PanelH = 157, Inset = 5;
    static float CellW => (PanelW - Inset * 2) / 10f;
    static float CellH => (PanelH - Inset * 2) / 10f;
    static readonly Dictionary<int, Vector2> _cellAt = new();

    /// <summary>One stone panel of 100 cells with its range in big green outline numbers behind.</summary>
    static void Panel(int first, float x)
    {
        if (first > Count) return;
        var panel = Box(x, PanelY, PanelW, PanelH, Stone, StoneEdge, 5);
        // the grid lines
        for (int i = 0; i <= 10; i++)
        {
            panel.AddChild(new ColorRect { Position = P(Inset + i * CellW, Inset), Size = new Vector2(Math.Max(1, _px * 0.6f), CellH * 10 * _px), Color = new Color(0.52f, 0.5f, 0.46f, 0.9f), MouseFilter = Control.MouseFilterEnum.Ignore });
            panel.AddChild(new ColorRect { Position = P(Inset, Inset + i * CellH), Size = new Vector2(CellW * 10 * _px, Math.Max(1, _px * 0.6f)), Color = new Color(0.52f, 0.5f, 0.46f, 0.9f), MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        int last = Math.Min(first + 99, Count);
        // "001 - 100" in FM's outlined green digits
        Label Big(string s, float y)
        {
            var l = new Label { Text = s, HorizontalAlignment = HorizontalAlignment.Center, Position = P(0, y), Size = P(PanelW, 46), MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Arial Black", "Arial", "Verdana" }, FontWeight = 900 });
            l.AddThemeFontSizeOverride("font_size", (int)(42 * _px));
            l.AddThemeColorOverride("font_color", new Color(0, 0, 0, 0));
            l.AddThemeColorOverride("font_outline_color", new Color(0.2f, 0.75f, 0.15f, 0.85f));
            l.AddThemeConstantOverride("outline_size", (int)(2.2f * _px));
            panel.AddChild(l);
            return l;
        }
        Big($"{first:000}", 8);
        Big("-", 52);
        Big($"{(first == 701 ? last : first + 99):000}", 96);
        for (int id = first; id <= last; id++)
        {
            int n = id - first, cx = n % 10, cy = n / 10, card = id;
            var at = new Vector2(x + Inset + cx * CellW, PanelY + Inset + cy * CellH);
            _cellAt[id] = at;
            // a small card in the cell: FM marks the cards you have this way
            var icon = new Panel { Position = P(Inset + cx * CellW + CellW * 0.18f, Inset + cy * CellH + CellH * 0.12f), Size = P(CellW * 0.64f, CellH * 0.76f), MouseFilter = Control.MouseFilterEnum.Ignore };
            icon.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.06f), BorderColor = new Color(0.95f, 0.72f, 0.12f), BorderWidthLeft = Math.Max(1, (int)(_px * 0.7f)), BorderWidthRight = Math.Max(1, (int)(_px * 0.7f)), BorderWidthTop = Math.Max(1, (int)(_px * 0.7f)), BorderWidthBottom = Math.Max(1, (int)(_px * 0.7f)) });
            icon.AddChild(new ColorRect { Position = P(CellW * 0.08f, CellH * 0.5f), Size = P(CellW * 0.22f, CellH * 0.14f), Color = new Color(0.9f, 0.2f, 0.1f), MouseFilter = Control.MouseFilterEnum.Ignore });
            icon.AddChild(new ColorRect { Position = P(CellW * 0.32f, CellH * 0.56f), Size = P(CellW * 0.22f, CellH * 0.06f), Color = new Color(0.95f, 0.72f, 0.12f), MouseFilter = Control.MouseFilterEnum.Ignore });
            panel.AddChild(icon);
            var hit = new Button { Flat = true, Position = P(Inset + cx * CellW, Inset + cy * CellH), Size = P(CellW, CellH), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("hover", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            hit.MouseEntered += () => { if (_cursor != card) { _cursor = card; FmSfx.Play(FmSfx.Flip); Hover(card); } };
            hit.Pressed += () => { _cursor = card; Forward(); };
            panel.AddChild(hit);
        }
    }

    static void Hover(int id)
    {
        if (!_cellAt.TryGetValue(id, out var at) || _crossH == null || _crossV == null || _glass == null) return;
        var c = P(at.X + CellW / 2, at.Y + CellH / 2);
        _crossH.Position = new Vector2(0, c.Y);
        _crossV.Position = new Vector2(c.X, 0);
        _glass.Position = P(at.X + CellW * 0.3f, at.Y + CellH * 0.3f);
        SetBar(id, true);
    }

    // ---------------- the card ----------------
    static void CardPage()
    {
        var info = FmData.Get(_cursor);
        // the card on the left, as big as FM shows it
        var holder = new Control { Position = P(70, 101), MouseFilter = Control.MouseFilterEnum.Ignore };
        var face = FmCardFace.Create(_cursor);
        float k = 200 * _px / FmCardFace.Size.Y;
        holder.Scale = new Vector2(k, k);
        holder.AddChild(face);
        _screen!.AddChild(holder);
        // type
        Box(144, 4, 170, 24, Navy, StoneEdge);
        string type = FmData.TypeNames[(int)info.Type];
        Text(type, 158, 10, 1.3f);
        // Guardian Stars
        Box(144, 31, 170, 56, Navy, StoneEdge);
        if (info.IsMonster)
        {
            Text("GUARDIAN STAR", 148, 35, 1.15f);
            int row = 0;
            foreach (int g in new[] { info.GuardianStar1, info.GuardianStar2 })
            {
                if (g <= 0 || g >= StarNames.Length) continue;
                var sign = new Label { Text = StarSigns[g], Position = P(158, 49 + row * 16), MouseFilter = Control.MouseFilterEnum.Ignore };
                sign.AddThemeFontSizeOverride("font_size", (int)(13 * _px));
                sign.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.92f));
                _screen.AddChild(sign);
                Text(StarNames[g], 178, 52 + row * 16, 1.15f);
                row++;
            }
        }
        // its text, typed out like FM
        Box(144, 90, 170, 110, Navy, StoneEdge);
        var lines = FmTextBox.Wrap(string.Join(" ", info.Desc.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())), 19);
        var glyphs = new List<CanvasItem>();
        for (int i = 0; i < lines.Count && i < 8; i++)
        {
            var t = Text(lines[i], 148, 95 + i * 13, 1.08f);
            foreach (var g in t.GetChildren().OfType<CanvasItem>()) { g.Visible = false; glyphs.Add(g); }
        }
        TypeOut(glyphs, _screen);
        if (info.IsMonster)
        {
            var stats = Text($"ATK {info.Atk}  DEF {info.Def}  Lv {info.Level}", 148, 186, 0.95f, new Color(1f, 0.85f, 0.4f));
        }
    }

    static async void TypeOut(List<CanvasItem> glyphs, Control screen)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        foreach (var g in glyphs)
        {
            if (!GodotObject.IsInstanceValid(screen) || !GodotObject.IsInstanceValid(g)) return;
            g.Visible = true;
            var t = tree.CreateTimer(0.012);
            await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout);
        }
    }

    // ---------------- the model ----------------
    static void ModelPage()
    {
        // the wireframe room with the monster attacking a dummy
        var room = new FmMonsterStage(_cursor, monsterX: 560, dummyX: 930);
        room.View.Position = Vector2.Zero;
        room.View.Size = P(320, 203);
        _screen!.AddChild(room.View);
        // the card turned on its side at the left (a slanted picture of it)
        var vp = new SubViewport { Size = new Vector2I((int)FmCardFace.Size.X, (int)FmCardFace.Size.Y), TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var face = FmCardFace.Create(_cursor);
        face.Position = Vector2.Zero;
        vp.AddChild(face);
        _screen.AddChild(vp);
        var tex = vp.GetTexture();
        var w = FmCardFace.Size.X; var h = FmCardFace.Size.Y;
        var card = new Polygon2D
        {
            Texture = tex,
            Polygon = new[] { P(-6, -6), P(78, 6), P(78, 196), P(-6, 210) },
            UV = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) },
        };
        _screen.AddChild(card);
        // its edge, for thickness
        _screen.AddChild(new Polygon2D { Color = new Color(0.35f, 0.3f, 0.2f), Polygon = new[] { P(78, 6), P(81, 8), P(81, 194), P(78, 196) } });
    }
}

/// <summary>
/// The main menu's Library: the screen fades out as when a run starts, then on black you choose Forbidden Memories'
/// Library or the simple one (search, filters and the fusion calculator).
/// </summary>
public static class FmLibraryChoice
{
    static CanvasLayer? _layer;
    public static bool Open => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.IsInsideTree();

    public static async void Show()
    {
        if (Open || FmLibrary.Open || FmLibraryFm.Open || NGame.Instance is not { } game) return;
        try
        {
            await game.Transition.FadeOut();
            _layer = new CanvasLayer { Layer = 112 };
            game.AddChild(_layer);
            var view = _layer.GetViewport().GetVisibleRect().Size;
            var black = new ColorRect { Color = Colors.Black, Size = view, MouseFilter = Control.MouseFilterEnum.Stop };
            _layer.AddChild(black);
            FmMusic.Play(FmLibrary.MusicTrack);
            var fadeIn = game.Transition.FadeIn(0.4f);
            float px = view.Y / 240f * 1.4f;
            string[] options = { "FORBIDDEN MEMORIES", "SIMPLE LIBRARY", "BACK" };
            float lineH = 16 * px, w = options.Max(o => FmTextBox.TextWidth(o, px)) + 30 * px, hgt = options.Length * lineH + 16 * px;
            var panel = new Control { Position = (view - new Vector2(w, hgt)) / 2, Size = new Vector2(w, hgt), MouseFilter = Control.MouseFilterEnum.Ignore };
            float cut = hgt * 0.14f;
            panel.AddChild(new Polygon2D
            {
                Color = new Color(0.06f, 0.06f, 0.08f),
                Polygon = new[] { new Vector2(cut, 0), new Vector2(w - cut, 0), new Vector2(w, cut), new Vector2(w, hgt - cut), new Vector2(w - cut, hgt), new Vector2(cut, hgt), new Vector2(0, hgt - cut), new Vector2(0, cut) },
            });
            var title = FmTextBox.Text("LIBRARY", px * 1.2f, new Color(1f, 0.85f, 0.35f));
            title.Position = new Vector2((view.X - FmTextBox.TextWidth("LIBRARY", px * 1.2f)) / 2, panel.Position.Y - 26 * px);
            _layer.AddChild(title);
            _layer.AddChild(panel);
            int picked = -1;
            var bars = new List<ColorRect>();
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var bar = new ColorRect { Position = new Vector2(cut * 0.5f, 8 * px + i * lineH - 2 * px), Size = new Vector2(w - cut, 12 * px + 4 * px), Color = new Color(0.86f, 0.9f, 0.1f, 0.9f), Visible = i == 0, MouseFilter = Control.MouseFilterEnum.Ignore };
                panel.AddChild(bar); bars.Add(bar);
                var t = FmTextBox.Text(options[i], px);
                t.Position = new Vector2((w - FmTextBox.TextWidth(options[i], px)) / 2, 8 * px + i * lineH);
                panel.AddChild(t);
                var hit = new Button { Flat = true, Position = bar.Position, Size = bar.Size, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("hover", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
                hit.MouseEntered += () => { for (int k = 0; k < bars.Count; k++) bars[k].Visible = k == idx; FmSfx.Play(FmSfx.Flip); };
                hit.Pressed += () => picked = idx;
                panel.AddChild(hit);
            }
            await fadeIn;
            var tree = game.GetTree();
            while (picked < 0 && Open)
            {
                if (Input.IsActionJustPressed("ui_cancel")) picked = 2;
                await game.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            FmSfx.Play(FmSfx.Mark);
            var layer = _layer;
            if (picked == 0) { FmLibraryFm.Show(); Free(layer); }
            else if (picked == 1) { FmLibrary.Show(); Free(layer); }
            else { _layer = null; await FadeAway(layer!); FmMusic.Play(FmMedia.MenuTrack); }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] library choice: " + ex); if (_layer != null) Free(_layer); }
    }

    static void Free(CanvasLayer? layer)
    {
        if (layer != null && GodotObject.IsInstanceValid(layer)) layer.QueueFree();
        if (layer == _layer) _layer = null;
    }

    /// <summary>Leaving a Library screen: through black and back to the menu.</summary>
    public static async Task FadeAway(CanvasLayer layer)
    {
        if (!GodotObject.IsInstanceValid(layer) || Engine.GetMainLoop() is not SceneTree tree) return;
        var black = new ColorRect { Color = Colors.Black, Size = layer.GetViewport().GetVisibleRect().Size, Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        layer.AddChild(black);
        var t = black.CreateTween();
        t.TweenProperty(black, "modulate:a", 1f, 0.35);
        await black.ToSignal(t, Tween.SignalName.Finished);
        foreach (var c in layer.GetChildren()) if (c != black) c.QueueFree();
        var t2 = black.CreateTween();
        t2.TweenProperty(black, "modulate:a", 0f, 0.5);
        await black.ToSignal(t2, Tween.SignalName.Finished);
        layer.QueueFree();
    }
}
