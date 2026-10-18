using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace FMDuelist.Game;

/// <summary>
/// Neow is never seen in Duel Master K runs: the starting choice is made in front of the palace at night with
/// Simon Muran standing there instead (FM pictures, local only), and Neow's name banner is hidden.
/// </summary>
[HarmonyPatch(typeof(NAncientEventLayout), "InitializeVisuals")]
static class FmNoNeowPatch
{
    /// <summary>Gives the game's own panel back (if FM's dialogue couldn't finish the choice).</summary>
    public static bool Released;

    /// <summary>Keeps the layout's dialogue and buttons hidden (the layout shows them again as its lines advance).</summary>
    static void Keep(Control content)
    {
        Released = false;
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        void Hide()
        {
            if (!GodotObject.IsInstanceValid(content) || !content.IsInsideTree()) { tree.ProcessFrame -= Hide; return; }
            if (Released) { content.Visible = true; tree.ProcessFrame -= Hide; return; }
            content.Visible = false;
        }
        tree.ProcessFrame += Hide;
        Hide();
    }

    static void Postfix(NAncientEventLayout __instance)
    {
        try
        {
            var t = Traverse.Create(__instance);
            if (t.Field("_ancientEvent").GetValue<AncientEventModel>() is not Neow neow || neow.Owner is not { } owner || !FmRules.IsFmPlayer(owner)) return;
            if (t.Field("_ancientBgContainer").GetValue<Control>() is { } bg)
            {
                foreach (var c in bg.GetChildren()) c.QueueFree();
                var place = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
                place.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                place.AddChild(new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
                // just the palace at night (no one in it), shown whole: a soft blurred copy fills the sides
                if (FmScene.Tex("bg_palace") is { } tex)
                {
                    var src = tex.GetImage();
                    var blur = (Image)src.Duplicate();
                    blur.Resize(Math.Max(1, src.GetWidth() / 12), Math.Max(1, src.GetHeight() / 12), Image.Interpolation.Bilinear);
                    blur.Resize(src.GetWidth(), src.GetHeight(), Image.Interpolation.Cubic);
                    var fill = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(0.5f, 0.5f, 0.55f) };
                    fill.Texture = ImageTexture.CreateFromImage(blur);
                    fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    place.AddChild(fill);
                    var r = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
                    r.Texture = tex;
                    r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    r.Material = new ShaderMaterial { Shader = new Shader { Code = @"shader_type canvas_item;
void fragment(){ vec4 c = texture(TEXTURE, UV) * COLOR; float e = smoothstep(0.0, 0.05, UV.x) * smoothstep(0.0, 0.05, 1.0 - UV.x); COLOR = vec4(c.rgb, c.a * e); }" } };
                    place.AddChild(r);
                }
                bg.AddChild(place);
            }
            if (t.Field("_ancientNameBanner").GetValue<Control>() is { } banner) banner.Visible = false;
            // no Slay the Spire dialogue box or buttons: the choice is made in FM's own dialogue (Simon's scene)
            if (t.Field("_contentContainer").GetValue<Control>() is { } content) Keep(content);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] hide Neow: " + ex.Message); }
    }
}
