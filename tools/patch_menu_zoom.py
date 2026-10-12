import os
H = os.path.expanduser('~/fmsts')
p = f'{H}/src/FMDuelist/Game/FmIntro.cs'
s = open(p, encoding='utf-8').read()
a = s[s.index('        var art = new TextureRect\n        {\n            Name = "FMDuelistTitle"'):s.index('        bg.AddChild(shade);\n') + len('        bg.AddChild(shade);\n')]
b = '''        // soft fill: the same frame blurred and darkened, covering the whole screen so there are no bars
        var src = _title.GetImage();
        var blur = (Image)src.Duplicate();
        blur.Resize(src.GetWidth() / 10, src.GetHeight() / 10, Image.Interpolation.Bilinear);
        blur.Resize(src.GetWidth(), src.GetHeight(), Image.Interpolation.Cubic);
        var fill = new TextureRect
        {
            Name = "FMDuelistTitle", Texture = ImageTexture.CreateFromImage(blur), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(0.45f, 0.4f, 0.38f),
        };
        fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.AddChild(fill);
        // the whole frame (zoomed out), centred, sitting above the menu buttons
        var art = new TextureRect
        {
            Name = "FMDuelistLogo", Texture = _title, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        art.AnchorLeft = 0; art.AnchorRight = 1; art.AnchorTop = 0.02f; art.AnchorBottom = 0.86f;
        art.OffsetLeft = art.OffsetRight = art.OffsetTop = art.OffsetBottom = 0;
        bg.AddChild(art);
        // feather the frame's edges into the fill
        var edge = new Shader { Code = @"shader_type canvas_item;
void fragment(){ vec4 c = texture(TEXTURE, UV) * COLOR; float e = smoothstep(0.0, 0.06, UV.x) * smoothstep(0.0, 0.06, 1.0 - UV.x) * smoothstep(0.0, 0.06, UV.y) * smoothstep(0.0, 0.06, 1.0 - UV.y); COLOR = vec4(c.rgb, c.a * e); }" };
        art.Material = new ShaderMaterial { Shader = edge };
        var shade = new ColorRect { Name = "FMDuelistShade", Color = new Color(0, 0, 0, 0.15f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.AddChild(shade);
'''
s = s.replace(a, b, 1)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
