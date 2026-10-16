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
                case MouseButton.WheelUp: if (_page == Page.Table) Turn(-1); return true;
                case MouseButton.WheelDown: if (_page == Page.Table) Turn(1); return true;
            }
            return false;
        }
        if (e is InputEventKey { Pressed: true } k)
        {
            switch (k.Keycode)
            {
                case Key.Escape or Key.Backspace: Back(); return true;
                case Key.Enter or Key.KpEnter or Key.Space: Forward(); return true;
                case Key.Left: if (_page != Page.Model) Step(-1); return true;
                case Key.Right: if (_page != Page.Model) Step(1); return true;
                case Key.Up: if (_page == Page.Table) Step(-10); return true;
                case Key.Down: if (_page == Page.Table) Step(10); return true;
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

    static bool _busy;

    static async void Back()
    {
        if (_busy) return;
        FmSfx.Play(FmSfx.Flip);
        switch (_page)
        {
            case Page.Model:
                _busy = true;
                try { await CloseModel(); } catch (Exception ex) { Log.Warn("[FMDuelist] library close card: " + ex.Message); _page = Page.Card; Render(); }
                _busy = false;
                break;
            case Page.Card:
                _busy = true;
                try { await FlyBack(); } catch (Exception ex) { Log.Warn("[FMDuelist] library card out: " + ex.Message); }
                _busy = false;
                _page = Page.Table; _row = (_cursor - 1) / 200; Render();
                break;
            default: Close(); break;
        }
    }

    static void Forward()
    {
        if (_busy) return;
        FmSfx.Play(FmSfx.Mark);
        switch (_page)
        {
            case Page.Table: _page = Page.Card; _intro = true; Render(); break;
            case Page.Card: if (FmData.Get(_cursor).IsMonster) OpenModel(); break;
            default: Back(); break;
        }
    }

    static void Step(int d)
    {
        if (_busy) return;
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
        Build();
    }

    /// <summary>Builds the current page on a new screen (on top of whatever is there) and returns it.</summary>
    static Control Build()
    {
        _screen = new Control { Size = _stage!.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
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
        return _screen;
    }

    /// <summary>FM's stone bar along the bottom: the card's number on a black tab, its name below.</summary>
    static void Bar(bool showName, bool flag)
    {
        var bar = new Control { Position = P(0, 203), Size = P(320, 37), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (FmScene.Tex("textbox") is { } tb)
        {
            var stone = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
            stone.Texture = tb; stone.Size = P(320, 37);
            bar.AddChild(stone);
        }
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
            var l = new Label { Text = s, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Position = P(0, y), Size = P(PanelW, 62), MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Arial Black", "Arial", "Verdana" }, FontWeight = 900 });
            l.AddThemeFontSizeOverride("font_size", (int)(60 * _px));
            l.AddThemeConstantOverride("line_spacing", 0);
            l.AddThemeColorOverride("font_color", new Color(0, 0, 0, 0));
            l.AddThemeColorOverride("font_outline_color", new Color(0.2f, 0.75f, 0.15f, 0.85f));
            l.AddThemeConstantOverride("outline_size", (int)(2.2f * _px));
            panel.AddChild(l);
            return l;
        }
        Big($"{first:000}", 2);
        Big("-", 46);
        Big($"{(first == 701 ? last : first + 99):000}", 92);
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
    static bool _intro;
    static Control? _holder, _face, _back;
    static readonly List<Control> _right = new();
    static List<CanvasItem> _glyphs = new();
    static float CardScale => 200 * _px / FmCardFace.Size.Y;
    static Vector2 CardHome => P(70, 101);

    static void CardPage()
    {
        var info = FmData.Get(_cursor);
        // the card on the left, as big as FM shows it (with its back, for the flip)
        _holder = new Control { Position = CardHome, MouseFilter = Control.MouseFilterEnum.Ignore };
        _holder.Scale = new Vector2(CardScale, CardScale);
        _face = FmCardFace.Create(_cursor);
        _holder.AddChild(_face);
        // (ExpandMode before Size: otherwise the picture's own 276x388 locks in as its smallest size)
        var back = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        back.Texture = FmRivalHand.Back();
        back.Size = FmCardFace.Size;
        back.Position = -FmCardFace.Size / 2;
        _back = back;
        _holder.AddChild(_back);
        _screen!.AddChild(_holder);
        int before = _screen.GetChildCount();
        // type
        Box(144, 4, 170, 24, Navy, StoneEdge);
        Text(FmData.TypeNames[(int)info.Type], 158, 10, 1.3f);
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
        // its text, typed out like FM once the card is in place
        Box(144, 90, 170, 110, Navy, StoneEdge);
        var lines = FmTextBox.Wrap(string.Join(" ", info.Desc.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim())), 19);
        _glyphs = new List<CanvasItem>();
        for (int i = 0; i < lines.Count && i < 8; i++)
        {
            var t = Text(lines[i], 148, 95 + i * 13, 1.08f);
            foreach (var g in t.GetChildren().OfType<CanvasItem>()) { g.Visible = false; _glyphs.Add(g); }
        }
        _right.Clear();
        for (int i = before; i < _screen.GetChildCount(); i++) if (_screen.GetChild(i) is Control c) _right.Add(c);
        if (_intro) { _intro = false; FlyIn(); }
        else if (!_closing) TypeOut(_glyphs, _screen);
    }

    /// <summary>Where the card's cell is on the table (the card flies from and back to it).</summary>
    static Vector2 CellCenter()
    {
        int n = (_cursor - 1) % 200, panel = n / 100, k = n % 100;
        float x = (panel == 0 ? 3 : 162) + Inset + (k % 10 + 0.5f) * CellW, y = PanelY + Inset + (k / 10 + 0.5f) * CellH;
        return P(x, y);
    }

    static float CellScale => CellH * 0.8f * _px / FmCardFace.Size.Y;

    /// <summary>
    /// FM: the card leaves its cell face down, tumbles up to its place on the left trailing outlines, while the
    /// blue boxes slide in from the right; then it flips over and its text types out.
    /// </summary>
    static async void FlyIn()
    {
        if (_holder == null || _face == null || _back == null || Engine.GetMainLoop() is not SceneTree tree) return;
        _busy = true;
        var holder = _holder; var screen = _screen!;
        try
        {
            _face.Visible = false; _back.Visible = true;
            holder.Position = CellCenter();
            holder.Scale = Vector2.One * CellScale;
            holder.Rotation = -1.1f;
            foreach (var c in _right) { var home = c.Position; c.Position = home + new Vector2(_sw * 0.7f, 0); c.SetMeta("home", home); }
            Whoosh(0.35f);
            var t = holder.CreateTween().SetParallel();
            t.TweenProperty(holder, "position", CardHome, 0.42).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            t.TweenProperty(holder, "scale", Vector2.One * CardScale, 0.42).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            t.TweenProperty(holder, "rotation", 0f, 0.42).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            foreach (var c in _right) t.TweenProperty(c, "position", (Vector2)c.GetMeta("home"), 0.38).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            Trail(holder, screen, t);
            await holder.ToSignal(t, Tween.SignalName.Finished);
            await Flip(holder, toFace: true);
            if (GodotObject.IsInstanceValid(screen)) TypeOut(_glyphs, screen);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] library card in: " + ex.Message); }
        finally { _busy = false; }
    }

    /// <summary>Going back: the card turns face down and flies back into its cell, the boxes slide away.</summary>
    static async Task FlyBack()
    {
        if (_holder == null || !GodotObject.IsInstanceValid(_holder)) return;
        var holder = _holder; var screen = _screen!;
        await Flip(holder, toFace: false);
        if (!GodotObject.IsInstanceValid(holder)) return;
        Whoosh(0.3f);
        var t = holder.CreateTween().SetParallel();
        t.TweenProperty(holder, "position", CellCenter(), 0.36).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        t.TweenProperty(holder, "scale", Vector2.One * CellScale, 0.36).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        t.TweenProperty(holder, "rotation", 1.1f, 0.36).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        foreach (var c in _right) if (GodotObject.IsInstanceValid(c)) t.TweenProperty(c, "position", c.Position + new Vector2(_sw * 0.7f, 0), 0.32).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        Trail(holder, screen, t);
        await holder.ToSignal(t, Tween.SignalName.Finished);
    }

    /// <summary>FM's card flip: it turns edge-on, shows its other side and turns back.</summary>
    static async Task Flip(Control holder, bool toFace)
    {
        if (_face == null || _back == null) return;
        float k = holder.Scale.Y;
        var a = holder.CreateTween();
        a.TweenProperty(holder, "scale:x", 0f, 0.11).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        await holder.ToSignal(a, Tween.SignalName.Finished);
        if (!GodotObject.IsInstanceValid(holder)) return;
        _face.Visible = toFace; _back.Visible = !toFace;
        FmSfx.Play(FmSfx.Flip);
        var b = holder.CreateTween();
        b.TweenProperty(holder, "scale:x", k, 0.13).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        await holder.ToSignal(b, Tween.SignalName.Finished);
    }

    /// <summary>The trailing outlines FM leaves behind a flying card: copies of its edge that fade as it moves on.</summary>
    static async void Trail(Control holder, Control screen, Tween moving)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var half = FmCardFace.Size / 2;
        var corners = new[] { new Vector2(-half.X, -half.Y), new Vector2(half.X, -half.Y), new Vector2(half.X, half.Y), new Vector2(-half.X, half.Y) };
        while (GodotObject.IsInstanceValid(holder) && GodotObject.IsInstanceValid(screen) && moving.IsValid() && moving.IsRunning())
        {
            var pts = corners.Select(c => holder.Position + (c * holder.Scale).Rotated(holder.Rotation)).ToArray();
            var ghost = new Line2D { Points = pts, Closed = true, Width = Math.Max(1.5f, _px * 0.9f), DefaultColor = new Color(Lime, 0.95f), Antialiased = true };
            screen.AddChild(ghost);
            screen.MoveChild(ghost, holder.GetIndex());
            var f = ghost.CreateTween();
            f.TweenProperty(ghost, "modulate:a", 0f, 0.28);
            f.TweenCallback(Callable.From(ghost.QueueFree));
            await holder.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await holder.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    // ---------------- the whoosh ----------------
    static AudioStreamWav? _whoosh;

    /// <summary>A rushing-air sweep (filtered noise rising then falling), made once in code.</summary>
    static void Whoosh(float volume = 0.6f)
    {
        try
        {
            if (_whoosh == null)
            {
                const int rate = 22050; int n = (int)(rate * 0.55);
                var data = new byte[n * 2];
                var rng = new Random(7);
                double low = 0, band = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / n;
                    double env = Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.7)), 2);
                    double cut = 0.02 + 0.25 * Math.Sin(Math.PI * t);         // the sweep
                    double x = rng.NextDouble() * 2 - 1;
                    low += cut * (x - low);
                    band += cut * (low - band);
                    short v = (short)Math.Clamp((low - band) * env * 52000, short.MinValue, short.MaxValue);
                    data[i * 2] = (byte)(v & 0xFF); data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                }
                _whoosh = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
            }
            if (_layer == null || !GodotObject.IsInstanceValid(_layer)) return;
            var player = new AudioStreamPlayer { Stream = _whoosh, VolumeDb = Mathf.LinearToDb(volume) };
            _layer.AddChild(player);
            player.Finished += player.QueueFree;
            player.Play();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] whoosh: " + ex.Message); }
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
        _room = room.View;
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
            Polygon = Swung,
            UV = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) },
        };
        _screen.AddChild(card);
        _swing = card;
        // its edge, for thickness
        _edge = new Polygon2D { Color = new Color(0.35f, 0.3f, 0.2f), Polygon = new[] { P(78, 6), P(81, 8), P(81, 194), P(78, 196) } };
        _screen.AddChild(_edge);
    }

    // ---------------- opening the card (FM: it swings open like a door onto the monster's room) ----------------
    static Polygon2D? _swing, _edge;
    static Control? _room;
    static bool _closing;

    /// <summary>The card lying flat where the card page shows it.</summary>
    static Vector2[] Shut
    {
        get
        {
            var c = CardHome; var half = FmCardFace.Size / 2 * CardScale;
            return new[] { c - half, c + new Vector2(half.X, -half.Y), c + half, c + new Vector2(-half.X, half.Y) };
        }
    }

    /// <summary>The card swung open on its left edge, as the monster view shows it.</summary>
    static Vector2[] Swung => new[] { P(-6, -6), P(78, 6), P(78, 196), P(-6, 210) };

    /// <summary>Between shut (0) and open (1); the right edge swings in an arc so it reads as turning, not sliding.</summary>
    static Vector2[] Between(float t)
    {
        var a = Shut; var b = Swung; var r = new Vector2[4];
        for (int i = 0; i < 4; i++) r[i] = a[i].Lerp(b[i], t);
        // as the door turns, its far edge first comes towards you (taller) before settling
        float bulge = Mathf.Sin(t * Mathf.Pi) * 10 * _px;
        r[1] += new Vector2(bulge * 0.6f, -bulge); r[2] += new Vector2(bulge * 0.6f, bulge);
        return r;
    }

    static void SetSwing(Polygon2D card, float t) { if (GodotObject.IsInstanceValid(card)) card.Polygon = Between(t); }

    /// <summary>Card page to monster view: whoosh, the blue boxes slide off right and the card swings open.</summary>
    static async void OpenModel()
    {
        if (_stage == null || _screen == null) return;
        _busy = true;
        try
        {
            var old = _screen; var oldRight = _right.ToList(); var oldHolder = _holder;
            Whoosh();
            _page = Page.Model;
            var sc = Build();
            _stage.MoveChild(sc, 0); // the room comes up behind the card page
            if (oldHolder != null) oldHolder.Visible = false;
            var card = _swing!; var room = _room!;
            if (_edge != null) _edge.Visible = false;
            SetSwing(card, 0);
            room.Modulate = new Color(1, 1, 1, 0);
            var t = card.CreateTween().SetParallel();
            t.TweenMethod(Callable.From<float>(v => SetSwing(card, v)), 0f, 1f, 0.55).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            t.TweenProperty(room, "modulate:a", 1f, 0.45).SetDelay(0.1);
            foreach (var c in oldRight) if (GodotObject.IsInstanceValid(c)) t.TweenProperty(c, "position", c.Position + new Vector2(_sw * 0.75f, 0), 0.4).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            await card.ToSignal(t, Tween.SignalName.Finished);
            if (GodotObject.IsInstanceValid(old)) old.QueueFree();
            if (_edge != null && GodotObject.IsInstanceValid(_edge)) _edge.Visible = true;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] library open card: " + ex.Message); }
        finally { _busy = false; }
    }

    /// <summary>Monster view back to the card page: the card swings shut and the blue boxes slide back in.</summary>
    static async Task CloseModel()
    {
        if (_stage == null || _screen == null || _swing == null) { _page = Page.Card; Render(); return; }
        var old = _screen; var card = _swing; var room = _room;
        Whoosh(0.45f);
        if (_edge != null) _edge.Visible = false;
        _page = Page.Card;
        _closing = true;
        Build();
        _closing = false;
        var holder = _holder!;
        holder.Visible = false;
        foreach (var c in _right) { var home = c.Position; c.Position = home + new Vector2(_sw * 0.75f, 0); c.SetMeta("home", home); }
        var t = card.CreateTween().SetParallel();
        t.TweenMethod(Callable.From<float>(v => SetSwing(card, v)), 1f, 0f, 0.5).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        if (room != null) t.TweenProperty(room, "modulate:a", 0f, 0.35);
        foreach (var c in _right) t.TweenProperty(c, "position", (Vector2)c.GetMeta("home"), 0.42).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        await card.ToSignal(t, Tween.SignalName.Finished);
        if (GodotObject.IsInstanceValid(holder)) holder.Visible = true;
        if (GodotObject.IsInstanceValid(old)) old.QueueFree();
        if (_screen != null) TypeOut(_glyphs, _screen);
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
