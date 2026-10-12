using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>FM-style attack slash: a bright white crescent streak with an additive glow cutting across the target, plus a flash.</summary>
public static class FmSlashVfx
{
    public static void Play(Vector2 screenPos)
    {
        if (NCombatRoom.Instance is null || Engine.GetMainLoop() is not SceneTree tree) return;
        var layer = new CanvasLayer { Layer = 60 };
        tree.Root.AddChild(layer);
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        var root = new Node2D { Position = screenPos, Rotation = -0.6f, Scale = new Vector2(0.35f, 0.35f) };
        layer.AddChild(root);

        root.AddChild(new Polygon2D { Polygon = Crescent(260, 34), Color = new Color(0.55f, 0.75f, 1f, 0.55f), Material = add });
        root.AddChild(new Polygon2D { Polygon = Crescent(240, 16), Color = new Color(1f, 1f, 1f, 0.95f), Material = add });
        var cross = new Node2D { Rotation = 1.25f };
        cross.AddChild(new Polygon2D { Polygon = Crescent(200, 12), Color = new Color(1f, 0.95f, 0.8f, 0.85f), Material = add });
        root.AddChild(cross);
        cross.Modulate = new Color(1, 1, 1, 0);

        var flash = new ColorRect { Color = new Color(1, 1, 1, 0.0f), MouseFilter = Control.MouseFilterEnum.Ignore, Size = layer.GetViewport().GetVisibleRect().Size };
        layer.AddChild(flash);

        var t = root.CreateTween();
        t.TweenProperty(root, "scale", new Vector2(1.15f, 1.15f), 0.08).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        t.Parallel().TweenProperty(flash, "color:a", 0.35f, 0.05);
        t.TweenProperty(cross, "modulate:a", 1f, 0.04);
        t.Parallel().TweenProperty(flash, "color:a", 0f, 0.15);
        t.TweenProperty(root, "modulate:a", 0f, 0.22);
        t.Parallel().TweenProperty(root, "scale", new Vector2(1.35f, 0.6f), 0.22);
        t.TweenCallback(Callable.From(layer.QueueFree));
    }

    /// <summary>Thin crescent: outer arc and inner arc meeting at sharp tips.</summary>
    static Vector2[] Crescent(float radius, float thickness)
    {
        const int n = 24;
        var pts = new List<Vector2>();
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.Lerp(-1.1f, 1.1f, i / (float)n);
            pts.Add(new Vector2(Mathf.Sin(a) * radius, -Mathf.Cos(a) * radius * 0.35f));
        }
        for (int i = n; i >= 0; i--)
        {
            float a = Mathf.Lerp(-1.1f, 1.1f, i / (float)n);
            float th = thickness * Mathf.Cos(a * 1.4f);
            pts.Add(new Vector2(Mathf.Sin(a) * radius, -Mathf.Cos(a) * radius * 0.35f + th));
        }
        return pts.ToArray();
    }
}
