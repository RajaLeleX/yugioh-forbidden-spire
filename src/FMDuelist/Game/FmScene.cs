using Godot;
using MegaCrit.Sts2.Core.Logging;
using FMDuelist.Data;

namespace FMDuelist.Game;

/// <summary>
/// A Forbidden Memories story scene on screen: FM's 4:3 picture above its stone text box, the game's own font and
/// typing (FmTextBox), clickable choices, fades and flashes. The pictures are local files in the mod's "story"
/// folder (never shipped); without them the scene still plays over black.
/// </summary>
public sealed class FmScene
{
    readonly CanvasLayer _layer;
    readonly Control _stage;
    readonly ColorRect _black, _flash;
    readonly TextureRect _pic, _picNext;
    readonly CanvasItem _boxBg;
    readonly FmTextBox _text;
    readonly SceneTree _tree;
    readonly float _sw, _sh;
    public bool Skipped;
    string? _shown;

    static string Dir => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmScene).Assembly.Location) ?? ".", "story");
    static readonly Dictionary<string, Texture2D?> _tex = new();

    public static Texture2D? Tex(string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        var f = System.IO.Path.Combine(Dir, name + ".png");
        t = System.IO.File.Exists(f) && Image.LoadFromFile(f) is { } img ? ImageTexture.CreateFromImage(img) : null;
        return _tex[name] = t;
    }

    FmScene(SceneTree tree)
    {
        _tree = tree;
        _layer = new CanvasLayer { Layer = 100 };
        tree.Root.AddChild(_layer);
        var view = _layer.GetViewport().GetVisibleRect().Size;
        _black = new ColorRect { Color = Colors.Black, Size = view, MouseFilter = Control.MouseFilterEnum.Stop, Modulate = new Color(1, 1, 1, 0) };
        _layer.AddChild(_black);
        _sw = Math.Min(view.X, view.Y * 4f / 3f); _sh = _sw * 3f / 4f;
        _stage = new Control { Position = new Vector2((view.X - _sw) / 2, (view.Y - _sh) / 2), Size = new Vector2(_sw, _sh), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.AddChild(_stage);
        float imgH = _sh * 700f / 1080f, boxH = _sh * 380f / 1080f;
        TextureRect Pic() => new() { Size = new Vector2(_sw, imgH), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _pic = Pic(); _picNext = Pic();
        _stage.AddChild(_pic); _stage.AddChild(_picNext);
        var boxRect = new Rect2(0, imgH, _sw, boxH);
        if (Tex("textbox") is { } tb)
            _boxBg = new TextureRect { Texture = tb, Position = boxRect.Position, Size = boxRect.Size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        else
        {
            var p = new Panel { Position = boxRect.Position, Size = boxRect.Size, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
            p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.1f, 0.11f, 0.2f), BorderColor = new Color(0.55f, 0.52f, 0.45f), BorderWidthBottom = 14, BorderWidthTop = 22, BorderWidthLeft = 14, BorderWidthRight = 14 });
            _boxBg = p;
        }
        _stage.AddChild((Node)_boxBg);
        _text = new FmTextBox(_stage, boxRect, _sh);
        _text.Root.Modulate = new Color(1, 1, 1, 0);
        _flash = new ColorRect { Color = Colors.White, Size = view, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        _layer.AddChild(_flash);
        _black.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _text.Click(); };
        var skip = new Button { Text = "Skip  ▶", Position = new Vector2(view.X - 190, 24), Size = new Vector2(160, 56), Modulate = new Color(1, 1, 1, 0.75f) };
        skip.AddThemeFontSizeOverride("font_size", 26);
        skip.Pressed += () => Skipped = true;
        _layer.AddChild(skip);
    }

    /// <summary>Fades the screen to the scene (black, stone text box).</summary>
    public static async Task<FmScene?> Open()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return null;
        var s = new FmScene(tree);
        Fade(s._black, 1, 0.5);
        await s.Wait(0.5);
        Fade((CanvasItem)s._boxBg, 1, 0.3); Fade(s._text.Root, 1, 0.3);
        return s;
    }

    public async Task Close()
    {
        _text.Clear();
        foreach (var c in _layer.GetChildren().OfType<CanvasItem>()) Fade(c, 0, 0.5);
        await Wait(0.55);
        if (GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
    }

    /// <summary>Hides the whole scene for a moment (e.g. while the game shows a card reward).</summary>
    public void SetVisible(bool v) => _layer.Visible = v;

    /// <summary>Cross-fades to another picture (FM fades through black between scenes).</summary>
    public async Task Show(string key, double fade = 0.45)
    {
        if (_shown == key) return;
        _text.Clear();
        if (_shown != null && _pic.Modulate.A > 0.01f) { Fade(_pic, 0, fade); await Wait(fade); }
        _pic.Texture = Tex(key);
        _shown = key;
        Fade(_pic, 1, fade);
        await Wait(fade);
    }

    public async Task Hide(double fade = 0.45)
    {
        _text.Clear();
        _shown = null;
        Fade(_pic, 0, fade);
        await Wait(fade);
    }

    /// <summary>One text box: a line from the disc by id, or a line written for the mod.</summary>
    public Task Say(string idOrText, bool literal = false)
    {
        if (Skipped) return Task.CompletedTask;
        string s = literal ? idOrText : FmAct1Story.Line(idOrText);
        return _text.Say(s, true, () => Skipped);
    }

    public Task<int> Choose(params string[] options) => _text.Choose(options);

    public async Task Flash()
    {
        var t = _flash.CreateTween();
        t.TweenProperty(_flash, "modulate:a", 1f, 0.15);
        t.TweenInterval(0.35);
        t.TweenProperty(_flash, "modulate:a", 0f, 0.9);
        await Wait(1.4);
    }

    /// <summary>The closing card: white text in the game's own font, fading in over black.</summary>
    public async Task Caption(string text, double hold)
    {
        await Hide(0.6);
        Fade((CanvasItem)_boxBg, 0, 0.6); Fade(_text.Root, 0, 0.6);
        var view = _layer.GetViewport().GetVisibleRect().Size;
        var l = new Label { Text = text, Size = new Vector2(view.X * 0.7f, view.Y * 0.4f), Position = new Vector2(view.X * 0.15f, view.Y * 0.3f), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (ResourceLoader.Exists("res://fonts/kreon_bold.ttf") && GD.Load<Font>("res://fonts/kreon_bold.ttf") is { } f) l.AddThemeFontOverride("font", f);
        l.AddThemeFontSizeOverride("font_size", (int)(view.Y * 0.04f));
        l.AddThemeColorOverride("font_color", new Color(0.96f, 0.93f, 0.85f));
        l.AddThemeConstantOverride("outline_size", 6);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        _layer.AddChild(l);
        await Wait(0.8);
        Fade(l, 1, 1.2);
        await Wait(1.2 + hold);
        // wait for a click (or the time running out)
        var done = false;
        _black.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) done = true; };
        for (double t = 0; t < 8 && !done; t += 0.05) await Wait(0.05);
        Fade(l, 0, 1.0);
        await Wait(1.0);
    }

    static void Fade(CanvasItem c, float to, double dur) { var tw = c.CreateTween(); tw.TweenProperty(c, "modulate:a", to, dur); }
    public async Task Wait(double s) { var t = _tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }
}
