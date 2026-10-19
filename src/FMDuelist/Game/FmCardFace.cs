using FMDuelist.Data;
using Godot;

namespace FMDuelist.Game;

/// <summary>A Yu-Gi-Oh-style card built from disc data: frame colored by card kind, name, level stars, art, ATK/DEF.</summary>
public static class FmCardFace
{
    public static readonly Vector2 Size = new(150, 218);

    static Color FrameColor(FmCardInfo c) => c.Type switch
    {
        FmType.Magic or FmType.Equip => new Color("1f8f6e"),
        FmType.Trap => new Color("a8457a"),
        FmType.Ritual => new Color("3b63b8"),
        _ => new Color("c9923e"),
    };

    static SystemFont Serif(bool bold) => new() { MultichannelSignedDistanceField = true, MsdfPixelRange = 16, FontNames = new[] { "Times New Roman", "Georgia", "Serif" }, FontWeight = bold ? 700 : 400 };

    /// <summary>A label with its own font, size and no extra line spacing (so it sits where it's put).</summary>
    static Label Fixed(string text, int size, Color color, Font font)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeConstantOverride("line_spacing", -1);
        l.AddThemeConstantOverride("outline_size", 0);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0));
        // no padding from the game's label style: the text sits exactly in its box
        l.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        return l;
    }

    /// <summary>Card centered on (0,0) so it can be rotated in place.</summary>
    public static Control Create(int id)
    {
        var info = FmData.Get(id);
        var root = new Control { Size = Size, Position = -Size / 2, PivotOffset = Size / 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        var frame = new Panel { Size = Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = FrameColor(info), BorderColor = new Color("2a1d10"),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        };
        frame.AddThemeStyleboxOverride("panel", style);
        root.AddChild(frame);

        var name = new Label { Text = info.Name, Position = new Vector2(8, 6), Size = new Vector2(134, 18), ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontOverride("font", Serif(true));
        name.AddThemeFontSizeOverride("font_size", 12);
        name.AddThemeConstantOverride("outline_size", 0);
        name.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        name.VerticalAlignment = VerticalAlignment.Center;
        name.AddThemeColorOverride("font_color", new Color("1a1208"));
        root.AddChild(name);

        if (info.IsMonster)
        {
            var stars = new Label { Text = new string('★', Math.Clamp(info.Level, 0, 12)), Position = new Vector2(8, 24), Size = new Vector2(134, 14), HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore };
            stars.AddThemeFontOverride("font", new SystemFont { MultichannelSignedDistanceField = true, MsdfPixelRange = 16, FontNames = new[] { "Segoe UI Symbol", "Arial Unicode MS", "Arial" } });
            stars.AddThemeFontSizeOverride("font_size", 10);
            stars.AddThemeConstantOverride("outline_size", 0);
            stars.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            stars.VerticalAlignment = VerticalAlignment.Center;
            stars.AddThemeColorOverride("font_color", new Color("d8281c"));
            root.AddChild(stars);
        }

        var artBg = new ColorRect { Color = new Color("2a1d10"), Position = new Vector2(12, 40), Size = new Vector2(126, 118), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(artBg);
        var art = new TextureRect
        {
            Texture = FmData.Art(id), Position = new Vector2(14, 42), Size = new Vector2(122, 114),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(art);

        // the bottom like FM's cards: a small framed text box, and ATK / DEF on two rows beside it (monsters);
        // fixed fonts and sizes so the game's theme can't push the lines out of place
        var ink = new Color("1a1208");
        string desc = string.Join(" ", info.Desc.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
        float boxW = info.IsMonster ? 66 : 130;
        var frameBox = new Panel { Position = new Vector2(10, 164), Size = new Vector2(boxW, 46), MouseFilter = Control.MouseFilterEnum.Ignore };
        frameBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("efe2c0"), BorderColor = new Color("e0761c"), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
        root.AddChild(frameBox);
        var text = Fixed(info.IsMonster ? desc : $"[{FmData.TypeNames[(int)info.Type]}]  {desc}", 5, ink, Serif(false));
        // wrapping on before the size, or the one long line sets the label's width
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart; text.ClipText = true;
        text.VerticalAlignment = VerticalAlignment.Top;
        text.Position = new Vector2(13, 166); text.Size = new Vector2(boxW - 6, 42);
        root.AddChild(text);
        if (info.IsMonster)
        {
            var statBox = new Panel { Position = new Vector2(79, 164), Size = new Vector2(61, 46), MouseFilter = Control.MouseFilterEnum.Ignore };
            statBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.08f), BorderColor = new Color("8a6a1c"), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
            root.AddChild(statBox);
            for (int i = 0; i < 2; i++)
            {
                // two equal rows filling the box; the word and the number share one size so they share a baseline
                float y = 165 + i * 22;
                var key = Fixed(i == 0 ? "ATK" : "DEF", 10, ink, Serif(true));
                key.Position = new Vector2(82, y); key.Size = new Vector2(20, 22);
                root.AddChild(key);
                // a wide slot ending inside the box's right edge, so even "3000" stays in
                var val = Fixed((i == 0 ? info.Atk : info.Def).ToString(), 10, ink, Serif(true));
                val.Position = new Vector2(100, y); val.Size = new Vector2(36, 22);
                val.HorizontalAlignment = HorizontalAlignment.Right;
                root.AddChild(val);
            }
        }
        return root;
    }
}
