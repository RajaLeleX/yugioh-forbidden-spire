using Godot;
using FMDuelist.Data;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' dialogue text: the game's own 8x12 font, read from the player's disc (WA_MRG 0xB48000), typed
/// out a letter at a time with FM's smeared "slide-in" look. Click once to show the whole box, click again to go on
/// (with a 0.4 s cooldown so boxes can't be skipped too fast). A red arrow blinks while it waits.
/// </summary>
public sealed class FmTextBox
{
    const int FontOffset = 0xB48000, SheetW = 256, SheetH = 96, CellW = 8, CellH = 12, LinePitch = 14;
    const double CharDelay = 0.035, ClickCooldown = 0.4;
    static readonly string[] Rows =
    {
        "!\"#$%&'()*+,-./0",
        "123456789:;<=>?",
        "ABCDEFGHIJKLMNOP",
        "QRSTUVWXYZ",
        "□abcdefghijklmno",
        "pqrstuvwxyz",
    };
    static ImageTexture? _sheet;
    static Dictionary<char, Rect2>? _glyphs;

    readonly Control _root, _textLayer;
    readonly Label _arrow;
    readonly float _px;            // screen pixels per PSX pixel
    readonly Vector2 _origin;      // where the first letter goes
    readonly Label? _fallback;
    double _lastClick = -10;
    bool _clicked;

    static void LoadFont()
    {
        if (_glyphs != null) return;
        _glyphs = new();
        var wa = FmData.Wa;
        if (wa == null || wa.Length < FontOffset + SheetW * SheetH / 2) return;
        var rgba = new byte[SheetW * SheetH * 4];
        for (int i = 0; i < SheetW * SheetH / 2; i++)
        {
            byte b = wa[FontOffset + i];
            for (int n = 0; n < 2; n++)
            {
                int v = n == 0 ? b & 15 : b >> 4, p = (i * 2 + n) * 4;
                rgba[p] = rgba[p + 1] = rgba[p + 2] = 255;
                rgba[p + 3] = (byte)(v * 17);
            }
        }
        _sheet = ImageTexture.CreateFromImage(Image.CreateFromData(SheetW, SheetH, false, Image.Format.Rgba8, rgba));
        for (int r = 0; r < Rows.Length; r++)
            for (int c = 0; c < Rows[r].Length; c++)
                _glyphs[Rows[r][c]] = new Rect2(c * CellW, r * CellH, CellW, CellH);
    }

    /// <param name="parent">The 4:3 stage.</param>
    /// <param name="box">The text box area on the stage (FM: the bottom 84 of 240 lines).</param>
    public FmTextBox(Control parent, Rect2 box, float stageHeight)
    {
        LoadFont();
        _px = stageHeight / 240f;
        _root = new Control { Position = box.Position, Size = box.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(_root);
        _origin = new Vector2(16 * _px, 28 * _px);
        _textLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_textLayer);
        if (_sheet == null)
        {
            _fallback = new Label { Position = _origin, Size = new Vector2(box.Size.X - 32 * _px, box.Size.Y), MouseFilter = Control.MouseFilterEnum.Ignore };
            _fallback.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Lucida Console", "Consolas", "Courier New" }, FontWeight = 600 });
            _fallback.AddThemeFontSizeOverride("font_size", (int)(10 * _px));
            _root.AddChild(_fallback);
        }
        _arrow = new Label { Text = "▼", Position = new Vector2(box.Size.X - 24 * _px, box.Size.Y - 26 * _px), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _arrow.AddThemeFontSizeOverride("font_size", (int)(8 * _px));
        _arrow.AddThemeColorOverride("font_color", new Color(0.95f, 0.2f, 0.1f));
        _root.AddChild(_arrow);
        var blink = _arrow.CreateTween().SetLoops();
        blink.TweenProperty(_arrow, "modulate:a", 0.25f, 0.35);
        blink.TweenProperty(_arrow, "modulate:a", 1f, 0.35);
    }

    public CanvasItem Root => _root;

    /// <summary>Feed the player's clicks here (the scene's input catcher).</summary>
    public void Click()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastClick < ClickCooldown) return;
        _lastClick = now;
        _clicked = true;
    }

    public void Clear()
    {
        foreach (var c in _textLayer.GetChildren()) c.QueueFree();
        if (_fallback != null) _fallback.Text = "";
        _arrow.Visible = false;
    }

    /// <summary>Types one box of dialogue, then waits for the player to click on (unless <paramref name="wait"/> is false).</summary>
    public async Task Say(string text, bool wait = true, Func<bool>? skipAll = null)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        Clear();
        _clicked = false;
        if (string.IsNullOrEmpty(text)) return;
        FmSfx.Play(FmSfx.Flip);
        var nodes = new List<Control>();
        int col = 0, line = 0;
        // lay out every letter first, hidden
        foreach (char ch in text)
        {
            if (ch == '\n') { line++; col = 0; continue; }
            var pos = _origin + new Vector2(col * CellW * _px, line * LinePitch * _px);
            col++;
            if (ch == ' ' || _sheet == null || _glyphs == null || !_glyphs.TryGetValue(ch, out var rect)) continue;
            var g = new TextureRect
            {
                Texture = new AtlasTexture { Atlas = _sheet, Region = rect }, Position = pos, Size = new Vector2(CellW, CellH) * _px,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            g.SetMeta("fm_col", col);
            _textLayer.AddChild(g);
            nodes.Add(g);
        }
        if (_fallback != null) { _fallback.Text = text; _fallback.VisibleCharacters = 0; }

        // type it out: each letter slides in from the right as a stretched smear and settles (FM's look)
        int shown = 0, total = _fallback != null ? text.Length : nodes.Count;
        while (shown < total)
        {
            if (_clicked || (skipAll?.Invoke() ?? false)) break;
            if (_fallback != null) _fallback.VisibleCharacters = ++shown;
            else Reveal(nodes[shown++], true);
            await Wait(tree, CharDelay);
        }
        // a click shows the whole box at once
        if (_fallback != null) _fallback.VisibleCharacters = -1;
        for (; shown < nodes.Count; shown++) Reveal(nodes[shown], false);
        _clicked = false;
        if (!wait) return;
        _arrow.Visible = true;
        while (!_clicked && !(skipAll?.Invoke() ?? false)) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        _clicked = false;
        _arrow.Visible = false;
    }

    /// <summary>
    /// A choice in FM's style: each option on its own line as "&lt;Option&gt;", the one under the mouse lit by FM's
    /// yellow bar. Click an option to pick it (the 0.4 s click cooldown applies). Returns the option's index.
    /// </summary>
    public async Task<int> Choose(IReadOnlyList<string> options)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || options.Count == 0) return 0;
        Clear();
        int picked = -1, hover = 0;
        var bars = new List<ColorRect>();
        float lineH = LinePitch * _px, width = _root.Size.X - _origin.X * 2;
        for (int i = 0; i < options.Count; i++)
        {
            int idx = i;
            var pos = _origin + new Vector2(-2 * _px, i * lineH - 1 * _px);
            var bar = new ColorRect { Position = pos, Size = new Vector2(width, CellH * _px + 2 * _px), Color = new Color(0.86f, 0.9f, 0.1f, 0.85f), Visible = i == 0, MouseFilter = Control.MouseFilterEnum.Ignore };
            _textLayer.AddChild(bar);
            bars.Add(bar);
            DrawLine("<" + options[i] + ">", _origin + new Vector2(0, i * lineH));
            var hit = new Button { Flat = true, Position = pos, Size = bar.Size, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("hover", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            hit.MouseEntered += () => { if (hover == idx) return; hover = idx; for (int k = 0; k < bars.Count; k++) bars[k].Visible = k == idx; FmSfx.Play(FmSfx.Flip); };
            hit.Pressed += () =>
            {
                double now = Time.GetTicksMsec() / 1000.0;
                if (now - _lastClick < ClickCooldown) return;
                _lastClick = now;
                picked = idx;
            };
            _textLayer.AddChild(hit);
        }
        FmSfx.Play(FmSfx.Flip);
        while (picked < 0) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        FmSfx.Play(FmSfx.Mark);
        _clicked = false;
        Clear();
        return picked;
    }

    /// <summary>
    /// FM's menu (the card shop's SAVE / BUILD DECK / ...): a black panel with the options centred in large letters,
    /// the one under the mouse on FM's yellow bar. Click to pick; returns the option's index.
    /// </summary>
    /// <param name="parent">Where the panel goes (the stage).</param>
    /// <param name="area">The panel's rectangle.</param>
    public async Task<int> Menu(Control parent, Rect2 area, IReadOnlyList<string> options)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || options.Count == 0) return 0;
        float px = _px * 1.6f, lineH = (CellH + 3) * px;
        var panel = new Control { Position = area.Position, Size = area.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        float cut = area.Size.Y * 0.12f, w = area.Size.X, h = area.Size.Y;
        panel.AddChild(new Polygon2D
        {
            Color = new Color(0, 0, 0, 0.94f),
            Polygon = new[] { new Vector2(cut, 0), new Vector2(w - cut, 0), new Vector2(w, cut), new Vector2(w, h - cut), new Vector2(w - cut, h), new Vector2(cut, h), new Vector2(0, h - cut), new Vector2(0, cut) },
        });
        parent.AddChild(panel);
        int picked = -1, hover = 0;
        var bars = new List<ColorRect>();
        float top = (h - options.Count * lineH) / 2;
        for (int i = 0; i < options.Count; i++)
        {
            int idx = i;
            var bar = new ColorRect { Position = new Vector2(cut * 0.4f, top + i * lineH - 1.5f * px), Size = new Vector2(w - cut * 0.8f, CellH * px + 3 * px), Color = new Color(0.86f, 0.9f, 0.1f, 0.9f), Visible = i == 0, MouseFilter = Control.MouseFilterEnum.Ignore };
            panel.AddChild(bar);
            bars.Add(bar);
            float textW = options[i].Length * CellW * px;
            DrawLine(options[i], new Vector2((w - textW) / 2, top + i * lineH), panel, px);
            var hit = new Button { Flat = true, Position = bar.Position, Size = bar.Size, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("hover", new StyleBoxEmpty()); hit.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            hit.MouseEntered += () => { if (hover == idx) return; hover = idx; for (int k = 0; k < bars.Count; k++) bars[k].Visible = k == idx; FmSfx.Play(FmSfx.Flip); };
            hit.Pressed += () =>
            {
                double now = Time.GetTicksMsec() / 1000.0;
                if (now - _lastClick < ClickCooldown) return;
                _lastClick = now;
                picked = idx;
            };
            panel.AddChild(hit);
        }
        FmSfx.Play(FmSfx.Flip);
        while (picked < 0 && GodotObject.IsInstanceValid(panel)) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        FmSfx.Play(FmSfx.Mark);
        if (GodotObject.IsInstanceValid(panel)) panel.QueueFree();
        return Math.Max(picked, 0);
    }

    /// <summary>One line of text in FM's font, shown at once (used for choices).</summary>
    void DrawLine(string s, Vector2 at, Control? parent = null, float px = 0)
    {
        parent ??= _textLayer;
        if (px <= 0) px = _px;
        if (_sheet == null || _glyphs == null)
        {
            var l = new Label { Text = s, Position = at, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", (int)(10 * px));
            parent.AddChild(l);
            return;
        }
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ' || !_glyphs.TryGetValue(s[i], out var rect)) continue;
            parent.AddChild(new TextureRect
            {
                Texture = new AtlasTexture { Atlas = _sheet, Region = rect }, Position = at + new Vector2(i * CellW * px, 0), Size = new Vector2(CellW, CellH) * px,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
    }

    void Reveal(Control g, bool animate)
    {
        g.Visible = true;
        if (!animate) return;
        var home = g.Position;
        g.Position = home + new Vector2(10 * _px, 0);
        g.Scale = new Vector2(1.9f, 1f);
        g.Modulate = new Color(1, 1, 1, 0.35f);
        var t = g.CreateTween().SetParallel();
        t.TweenProperty(g, "position", home, 0.09).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.TweenProperty(g, "scale", Vector2.One, 0.09);
        t.TweenProperty(g, "modulate:a", 1f, 0.09);
    }

    static async Task Wait(SceneTree tree, double s) { var t = tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }
}
