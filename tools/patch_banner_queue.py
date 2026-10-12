import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))
p = 'Game/FmRival.cs'
s = open(p, encoding='utf-8').read()
a = s[s.index('    public static void Banner(string text)'):]
a = a[:a.index('\n    }\n') + 7]
b = '''    static CanvasLayer? _bannerLayer;
    static Label? _bannerLabel;

    /// <summary>
    /// The gold commentary line under his face-down hand. Only one shows at a time: a new line first fades the old one
    /// out quickly, then fades in.
    /// </summary>
    public static void Banner(string text)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || NCombatRoom.Instance is not { } room) return;
        Log.Info("[FMDuelist] Duel: " + text);
        float delay = 0f;
        if (_bannerLabel != null && GodotObject.IsInstanceValid(_bannerLabel))
        {
            var old = _bannerLabel; var oldLayer = _bannerLayer;
            if (old.Modulate.A > 0.01f)
            {
                delay = 0.12f;
                var fade = old.CreateTween(); // CreateTween on a node replaces nothing, so stop its own timeline first
                foreach (var t in old.GetMeta("fm_tweens", new Godot.Collections.Array()).AsGodotArray()) if (t.AsGodotObject() is Tween tw0 && tw0.IsValid()) tw0.Kill();
                fade.TweenProperty(old, "modulate:a", 0f, delay);
                fade.TweenCallback(Callable.From(() => { if (oldLayer != null && GodotObject.IsInstanceValid(oldLayer)) oldLayer.QueueFree(); }));
            }
            else if (oldLayer != null && GodotObject.IsInstanceValid(oldLayer)) oldLayer.QueueFree();
        }
        var layer = new CanvasLayer { Layer = 75 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(view.X, 60), Position = new Vector2(0, 255), Modulate = new Color(1, 1, 1, 0) };
        label.AddThemeFontSizeOverride("font_size", 32);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        label.AddThemeConstantOverride("outline_size", 10);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer.AddChild(label);
        var tw = label.CreateTween();
        label.SetMeta("fm_tweens", new Godot.Collections.Array { tw });
        if (delay > 0) tw.TweenInterval(delay);
        tw.TweenProperty(label, "modulate:a", 1f, 0.15);
        tw.TweenInterval(1.4);
        tw.TweenProperty(label, "modulate:a", 0f, 0.3);
        tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }));
        _bannerLayer = layer; _bannerLabel = label;
    }
'''
s = s.replace(a, b, 1)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
