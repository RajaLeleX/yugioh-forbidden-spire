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
                foreach (var key in new[] { "bg_palace", "chr_simon_palace" })
                    if (FmScene.Tex(key) is { } tex)
                    {
                        var r = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore };
                        r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                        place.AddChild(r);
                    }
                bg.AddChild(place);
            }
            if (t.Field("_ancientNameBanner").GetValue<Control>() is { } banner) banner.Visible = false;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] hide Neow: " + ex.Message); }
    }
}
