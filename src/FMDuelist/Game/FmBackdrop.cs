using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>Field spells change the whole battlefield to a matching Slay the Spire location (falls back to the card's own scenery from the disc).</summary>
public static class FmBackdrop
{
    static Control? _root;
    static int _shown;

    static readonly Dictionary<int, Color> Tint = new()
    {
        [330] = new Color(0.10f, 0.30f, 0.12f, 0.35f), // Forest
        [331] = new Color(0.45f, 0.25f, 0.08f, 0.35f), // Wasteland
        [332] = new Color(0.25f, 0.32f, 0.45f, 0.30f), // Mountain
        [333] = new Color(0.35f, 0.42f, 0.10f, 0.30f), // Sogen
        [334] = new Color(0.05f, 0.22f, 0.50f, 0.35f), // Umi
        [335] = new Color(0.12f, 0.02f, 0.20f, 0.55f), // Yami
    };

    const string Blur = @"shader_type canvas_item;
uniform float radius = 2.5;
void fragment(){
  vec2 px = TEXTURE_PIXEL_SIZE * radius;
  vec4 c = vec4(0.0);
  for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) c += texture(TEXTURE, UV + vec2(float(x), float(y)) * px);
  COLOR = c / 25.0;
}";

    /// <summary>Each field card turns the battlefield into a Slay the Spire location.</summary>
    public static readonly Dictionary<int, string> Locations = new()
    {
        [330] = "overgrowth",            // Forest: the Overgrowth's woods
        [331] = "the_insatiable_boss",   // Wasteland: the sand worm's desert
        [332] = "waterfall_giant_boss",  // Mountain: cliffs and waterfall
        [333] = "the_kin_boss",          // Sogen: open grassland
        [334] = "underdocks",            // Umi: the sea-swept Underdocks
        [335] = "vantom_boss",           // Yami: ghostly darkness
    };

    /// <summary>Swap in the location's whole combat background (all its layers). Returns false if it can't be built.</summary>
    static bool SetLocation(int fieldId, Control container)
    {
        if (!Locations.TryGetValue(fieldId, out var key)) return false;
        try
        {
            var assets = new MegaCrit.Sts2.Core.Rooms.BackgroundAssets(key, new MegaCrit.Sts2.Core.Random.Rng((uint)(fieldId * 7919)));
            var bg = NCombatBackground.Create(assets);
            bg.Modulate = new Color(1, 1, 1, 0);
            bg.MouseFilter = Control.MouseFilterEnum.Ignore;
            container.AddChild(bg);
            _root = bg;
            var fade = bg.CreateTween();
            fade.TweenProperty(bg, "modulate:a", 1f, 0.8);
            MegaCrit.Sts2.Core.Logging.Log.Info($"[FMDuelist] Field {fieldId}: battlefield is now {key}");
            return true;
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn($"[FMDuelist] field location {key}: {ex.Message}"); return false; }
    }

    public static void Set(int fieldId)
    {
        var room = NCombatRoom.Instance;
        if (room?.Background?.GetParent() is not Control container) return;
        if (fieldId == _shown && _root != null && GodotObject.IsInstanceValid(_root)) return;
        var old = _root;
        _shown = fieldId;
        _root = null;
        if (old != null && GodotObject.IsInstanceValid(old))
        {
            var t = old.CreateTween();
            t.TweenProperty(old, "modulate:a", 0f, 0.6);
            t.TweenCallback(Callable.From(old.QueueFree));
        }
        if (fieldId == 0) return;
        if (SetLocation(fieldId, container)) return;
        if (FmData.Art(fieldId) is not { } art) return;

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var img = new TextureRect
        {
            Texture = art, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear, MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = new Shader { Code = Blur } },
        };
        img.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(img);
        var tint = new ColorRect { Color = Tint.GetValueOrDefault(fieldId, new Color(0, 0, 0, 0.3f)), MouseFilter = Control.MouseFilterEnum.Ignore };
        tint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(tint);
        var floor = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Ignore };
        floor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        floor.OffsetTop = -260;
        root.AddChild(floor);
        container.AddChild(root);
        _root = root;
        var fade = root.CreateTween();
        fade.TweenProperty(root, "modulate:a", 1f, 0.8);
    }

    public static void Clear() => Set(0);
}
