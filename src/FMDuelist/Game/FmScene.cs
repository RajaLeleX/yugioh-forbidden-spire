using Godot;
using MegaCrit.Sts2.Core.Logging;
using FMDuelist.Data;

namespace FMDuelist.Game;

/// <summary>
/// A Forbidden Memories story scene on screen, built in layers like FM's: a background plate, a character picture
/// that warps and slides in and out over it, the speaker's portrait box above the text box, and FM's stone text box
/// with the game's own font (FmTextBox). Every line shows who is speaking: a name plate, plus the portrait when the
/// speaker isn't the one pictured. The pictures are local files in the mod's "story" folder (never shipped); without
/// them the scene still plays over black.
/// </summary>
public sealed class FmScene
{
    readonly CanvasLayer _layer;
    readonly Control _stage, _picArea;
    readonly ColorRect _black, _flash;
    readonly TextureRect _bg, _chr, _icon;
    readonly PanelContainer _plate;
    readonly Label _plateText;
    readonly CanvasItem _boxBg;
    readonly FmTextBox _text;
    readonly SceneTree _tree;
    readonly float _sw, _sh, _imgH;
    public bool Skipped;
    Button? _skip;
    /// <summary>Scenes that aren't a cutscene (the card shop) have no Skip button.</summary>
    public void HideSkip() { if (_skip != null) _skip.Visible = false; }

    /// <summary>FM's black menu panel at the top left of the picture (the card shop's).</summary>
    public Task<int> Menu(params string[] options)
    {
        _plate.Visible = false;
        float s = _sw / 1440f, px = _sh / 240f;
        // tall enough for every option (FM's menu text is 1.6x the box text: 24 lines of pixels each)
        float h = Math.Max(240 * s, options.Length * 18 * px + 20 * px);
        float top = Math.Max(10 * s, Math.Min(195 * s, _imgH - h - 10 * s));
        return _text.Menu(_stage, new Rect2(35 * s, top, 605 * s, h), options);
    }
    string? _bgKey, _chrKey, _iconKey;
    /// <summary>Who is pictured right now (their lines need no portrait box).</summary>
    readonly HashSet<string> _onScreen = new();

    static string Dir => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmScene).Assembly.Location) ?? ".", "story");
    static readonly Dictionary<string, Texture2D?> _tex = new();

    public static Texture2D? Tex(string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        var f = System.IO.Path.Combine(Dir, name + ".png");
        t = System.IO.File.Exists(f) && Image.LoadFromFile(f) is { } img ? ImageTexture.CreateFromImage(img) : null;
        return _tex[name] = t;
    }

    /// <summary>FM's fade: the picture ripples sideways and dissolves (amt 1 = gone, 0 = settled).</summary>
    const string WarpShader = @"shader_type canvas_item;
uniform float amt = 1.0;
void fragment() {
    float w = amt * amt;
    vec2 uv = UV;
    uv.x += sin(UV.y * 70.0 + TIME * 30.0) * 0.045 * w;
    uv.y += sin(UV.x * 24.0 + TIME * 12.0) * 0.012 * w;
    vec4 c = texture(TEXTURE, uv);
    c.a *= clamp(1.0 - amt, 0.0, 1.0);
    COLOR = c;
}";
    static Shader? _warp;
    static ShaderMaterial Warp() => new() { Shader = _warp ??= new Shader { Code = WarpShader } };

    public static readonly Dictionary<string, string> Names = new()
    {
        ["teana"] = "Teana", ["jono"] = "Jono", ["seto"] = "Seto", ["simon"] = "Simon Muran", ["heishin"] = "Heishin",
        ["guard"] = "Guard", ["servant"] = "Servant", ["k"] = "K", ["v1"] = "Villager 1", ["v2"] = "Villager 2", ["v3"] = "Villager 3", ["shop"] = "Card Shop Owner",
    };

    FmScene(SceneTree tree)
    {
        _tree = tree;
        _layer = new CanvasLayer { Layer = 100 };
        tree.Root.AddChild(_layer);
        var view = _layer.GetViewport().GetVisibleRect().Size;
        _black = new ColorRect { Color = Colors.Black, Size = view, MouseFilter = Control.MouseFilterEnum.Stop, Modulate = new Color(1, 1, 1, 0) };
        _layer.AddChild(_black);
        _sw = Math.Min(view.X, view.Y * 4f / 3f); _sh = _sw * 3f / 4f;
        _stage = new Control { Position = new Vector2((view.X - _sw) / 2, (view.Y - _sh) / 2), Size = new Vector2(_sw, _sh), MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.AddChild(_stage);
        _imgH = _sh * 700f / 1080f;
        float boxH = _sh * 380f / 1080f;
        // the picture area clips pans and slides
        _picArea = new Control { Size = new Vector2(_sw, _imgH), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _stage.AddChild(_picArea);
        TextureRect Pic() => new() { Size = new Vector2(_sw, _imgH), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _bg = Pic(); _chr = Pic();
        _chr.Material = Warp(); _chr.Modulate = Colors.White; SetAmt(_chr, 1);
        _picArea.AddChild(_bg); _picArea.AddChild(_chr);
        // the speaker portrait box sits where FM puts it (right, just above the text box)
        float s = _sw / 1440f;
        _icon = new TextureRect { Position = new Vector2(1074 * s, (700 - 225 - 49) * s), Size = new Vector2(213, 225) * s, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, Material = Warp() };
        SetAmt(_icon, 1);
        _stage.AddChild(_icon);
        var boxRect = new Rect2(0, _imgH, _sw, boxH);
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
        // name plate: who is speaking, on the text box's top edge
        _plate = new PanelContainer { Position = new Vector2(28 * s, _imgH - 34 * s), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.11f, 0.22f, 0.95f), BorderColor = new Color(0.72f, 0.66f, 0.5f),
            BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
            ContentMarginLeft = 16 * s, ContentMarginRight = 16 * s, ContentMarginTop = 2 * s, ContentMarginBottom = 2 * s,
        });
        _plateText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _plateText.AddThemeFontOverride("font", new SystemFont { MultichannelSignedDistanceField = true, MsdfPixelRange = 16, FontNames = new[] { "Lucida Console", "Consolas", "Courier New" }, FontWeight = 700 });
        _plateText.AddThemeFontSizeOverride("font_size", (int)(30 * s));
        _plateText.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.35f));
        _plate.AddChild(_plateText);
        _stage.AddChild(_plate);
        _flash = new ColorRect { Color = Colors.White, Size = view, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        _layer.AddChild(_flash);
        _black.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _text.Click(); };
        var skip = _skip = new Button { Text = "Skip  ▶", Position = new Vector2(view.X - 190, 24), Size = new Vector2(160, 56), Modulate = new Color(1, 1, 1, 0.75f) };
        skip.AddThemeFontSizeOverride("font_size", 26);
        skip.Pressed += () => Skipped = true;
        _layer.AddChild(skip);
    }

    static void SetAmt(CanvasItem c, float v) => (c.Material as ShaderMaterial)?.SetShaderParameter("amt", v);
    Tween WarpTo(CanvasItem c, float to, double dur)
    {
        var mat = (ShaderMaterial)c.Material;
        float from = (float)mat.GetShaderParameter("amt");
        var t = c.CreateTween();
        t.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("amt", v)), from, to, dur).SetTrans(Tween.TransitionType.Sine);
        return t;
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

    public void SetVisible(bool v) => _layer.Visible = v;

    // ---------------- pictures ----------------
    /// <summary>A background plate (FM fades through black between places). Any character leaves first.</summary>
    public async Task Bg(string key, double fade = 0.45)
    {
        if (_bgKey == key && _bg.Size.Y <= _imgH + 1) return;
        _text.Clear(); HideSpeaker();
        if (_chrKey != null) await Chr(null);
        if (_bgKey != null && _bg.Modulate.A > 0.01f) { Fade(_bg, 0, fade); await Wait(fade); }
        _bg.Texture = Tex(key);
        _bg.Size = new Vector2(_sw, _imgH); _bg.Position = Vector2.Zero;
        _bgKey = key;
        Fade(_bg, 1, fade);
        await Wait(fade);
    }

    /// <summary>A full picture with its characters drawn in (shown like a background, people in it are on screen).</summary>
    public async Task Still(string key, params string[] who)
    {
        await Bg(key);
        _onScreen.Clear();
        foreach (var w in who) _onScreen.Add(w);
    }

    /// <summary>
    /// A character steps in: their picture slides a little from the side and settles out of FM's ripple, over the
    /// current background. A different character warps out first; null clears the character.
    /// </summary>
    public async Task Chr(string? key, params string[] who)
    {
        if (_chrKey == key) { if (key != null) { _onScreen.Clear(); foreach (var w in who) _onScreen.Add(w); } return; }
        HideSpeaker();
        if (_chrKey != null)
        {
            var o = _chr.CreateTween();
            o.TweenProperty(_chr, "position:x", -_sw * 0.05f, 0.35).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
            WarpTo(_chr, 1, 0.35);
            await Wait(0.37);
        }
        _chrKey = key;
        _onScreen.Clear();
        if (key == null) return;
        foreach (var w in who) _onScreen.Add(w);
        _chr.Texture = Tex(key);
        _chr.Position = new Vector2(_sw * 0.06f, 0);
        var t = _chr.CreateTween();
        t.TweenProperty(_chr, "position:x", 0f, 0.45).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        WarpTo(_chr, 0, 0.45);
        await Wait(0.47);
    }

    /// <summary>A tall picture that slowly pans up from its bottom to its top (FM's camera move).</summary>
    public async Task Pan(string key, double delay = 1.2, double dur = 2.6)
    {
        if (_chrKey != null) await Chr(null);
        _text.Clear(); HideSpeaker();
        if (_bg.Modulate.A > 0.01f) { Fade(_bg, 0, 0.45); await Wait(0.45); }
        var tex = Tex(key);
        float h = tex != null ? _sw * tex.GetHeight() / tex.GetWidth() : _imgH;
        _bg.Texture = tex; _bgKey = key;
        _bg.Size = new Vector2(_sw, h);
        _bg.Position = new Vector2(0, _imgH - h);
        _onScreen.Clear();
        Fade(_bg, 1, 0.45);
        var t = _bg.CreateTween();
        t.TweenInterval(delay);
        t.TweenProperty(_bg, "position:y", 0f, dur).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        await Wait(0.5);
    }

    /// <summary>Everything leaves (black picture area).</summary>
    public async Task Hide(double fade = 0.45)
    {
        _text.Clear(); HideSpeaker();
        if (_chrKey != null) await Chr(null);
        _bgKey = null;
        _onScreen.Clear();
        Fade(_bg, 0, fade);
        await Wait(fade);
    }

    // ---------------- who is speaking ----------------
    void HideSpeaker()
    {
        if (_iconKey != null) { WarpTo(_icon, 1, 0.25); _iconKey = null; }
        _plate.Visible = false;
    }

    void Speaker(string? who)
    {
        if (who == null) { HideSpeaker(); return; }
        _plateText.Text = Names.TryGetValue(who, out var n) ? n : who;
        _plate.Visible = true;
        // the portrait box shows whoever speaks from off the picture
        // the speaker's portrait always shows, so it's clear who is talking
        string? icon = who == "k" || Tex("icon_" + who) == null ? null : who;
        if (icon == _iconKey) return;
        if (icon == null) { WarpTo(_icon, 1, 0.25); _iconKey = null; return; }
        _icon.Texture = Tex("icon_" + icon);
        SetAmt(_icon, 1);
        WarpTo(_icon, 0, 0.3);
        _iconKey = icon;
    }

    /// <summary>Who says a disc line, from its id (tea_, jono_, seto_, simon_, hei_, serv_/guard_, isis_).</summary>
    public static string? SpeakerOf(string id) => id.Split('_')[0] switch
    {
        "tea" => "teana", "jono" => "jono", "seto" => "seto", "simon" => "simon", "hei" => "heishin",
        "serv" or "guard" => "guard", "isis" => "servant", "sm" => "simon", "te" => "teana", "jo" => "jono",
        "v1" => "v1", "v2" => "v2", "v3" => "v3", _ => null,
    };

    /// <summary>One text box: a line from the disc by id (speaker known), or a line written for the mod.</summary>
    public Task Say(string idOrText, string? speaker = null, bool literal = false)
    {
        if (Skipped) return Task.CompletedTask;
        string s = literal ? idOrText : FmAct1Story.Line(idOrText);
        Speaker(literal ? speaker : speaker ?? SpeakerOf(idOrText));
        return _text.Say(s, true, () => Skipped);
    }

    public Task Lit(string speaker, string text) => Say(text, speaker, true);

    public Task<int> Choose(params string[] options) { _plate.Visible = false; return _text.Choose(options); }

    /// <summary>
    /// A choice where each option is explained: while the mouse is on an option, an FM box over the picture shows
    /// what it is (e.g. what a starter relic includes). Returns the option's index.
    /// </summary>
    public async Task<int> ChooseInfo(IReadOnlyList<string> options, IReadOnlyList<string> infos, string heading)
    {
        _plate.Visible = false;
        float s = _sw / 1440f, px = _sh / 240f;
        var box = new Panel { Position = new Vector2(60 * s, 40 * s), Size = new Vector2(_sw - 120 * s, _imgH - 80 * s), MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.1f, 0.26f, 0.93f), BorderColor = new Color(0.72f, 0.66f, 0.5f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4, ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 6,
        });
        _stage.AddChild(box);
        var head = FmTextBox.Text(heading, px * 1.3f, new Color(1f, 0.86f, 0.35f));
        head.Position = new Vector2((box.Size.X - FmTextBox.TextWidth(heading, px * 1.3f)) / 2, 10 * px);
        box.AddChild(head);
        var body = new Control { Position = new Vector2(12 * px, 30 * px), MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(body);
        int cols = Math.Max(10, (int)((box.Size.X - 24 * px) / (8 * px)));
        void Show(int i)
        {
            foreach (var c in body.GetChildren()) c.QueueFree();
            if (i < 0 || i >= infos.Count) return;
            var lines = FmTextBox.Wrap(infos[i], cols);
            for (int k = 0; k < lines.Count && k < 9; k++)
            {
                var t = FmTextBox.Text(lines[k], px, k == 0 ? new Color(1f, 0.92f, 0.6f) : Colors.White);
                t.Position = new Vector2(0, k * 14 * px);
                body.AddChild(t);
            }
        }
        box.Modulate = new Color(1, 1, 1, 0);
        Fade(box, 1, 0.25);
        int r = await _text.Choose(options, Show);
        box.QueueFree();
        return r;
    }

    public async Task Flash()
    {
        var t = _flash.CreateTween();
        t.TweenProperty(_flash, "modulate:a", 1f, 0.15);
        t.TweenInterval(0.35);
        t.TweenProperty(_flash, "modulate:a", 0f, 0.9);
        await Wait(1.4);
    }

    /// <summary>The closing card: text in the game's own font, fading in over black.</summary>
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
        var done = false;
        _black.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) done = true; };
        for (double t = 0; t < 8 && !done; t += 0.05) await Wait(0.05);
        Fade(l, 0, 1.0);
        await Wait(1.0);
    }

    static void Fade(CanvasItem c, float to, double dur) { var tw = c.CreateTween(); tw.TweenProperty(c, "modulate:a", to, dur); }
    public async Task Wait(double s) { var t = _tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }
}
