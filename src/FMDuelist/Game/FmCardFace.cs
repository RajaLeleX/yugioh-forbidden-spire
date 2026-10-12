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
        name.AddThemeFontSizeOverride("font_size", 12);
        name.AddThemeColorOverride("font_color", new Color("1a1208"));
        root.AddChild(name);

        if (info.IsMonster)
        {
            var stars = new Label { Text = new string('★', Math.Clamp(info.Level, 0, 12)), Position = new Vector2(8, 24), Size = new Vector2(134, 14), HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore };
            stars.AddThemeFontSizeOverride("font_size", 10);
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

        var box = new ColorRect { Color = new Color("efe2c0"), Position = new Vector2(10, 164), Size = new Vector2(130, 46), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(box);
        var text = new Label
        {
            Text = info.IsMonster ? $"[{FmData.TypeNames[(int)info.Type]}]\nATK/{info.Atk}  DEF/{info.Def}" : $"[{FmData.TypeNames[(int)info.Type]}]",
            Position = new Vector2(14, 166), Size = new Vector2(122, 42), MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        text.AddThemeFontSizeOverride("font_size", 12);
        text.AddThemeColorOverride("font_color", new Color("1a1208"));
        root.AddChild(text);
        return root;
    }
}
