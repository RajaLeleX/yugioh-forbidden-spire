using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace FMDuelist.Game;

/// <summary>
/// The Necrobinder plays as Duel Master K: name and description everywhere, and his portrait on the top bar, the
/// character-select button and the character-select art. The portrait is a local file (duel_master_k.png next to the
/// mod) that is never shipped; without it the game's own art stays.
/// </summary>
public static class FmDuelMasterK
{
    public const string Name = "Duel Master K";

    static readonly Dictionary<string, string> Text = new()
    {
        ["NECROBINDER.title"] = Name,
        ["NECROBINDER.titleObject"] = Name,
        ["NECROBINDER.description"] =
            "A legendary duelist who never takes off his shades.\nHe summons [gold]Duel Monsters[/gold] from his cards and fuses them into ever stronger beasts to fight for him.",
        ["NECROBINDER.possessiveAdjective"] = "his",
        ["NECROBINDER.pronounObject"] = "him",
        ["NECROBINDER.pronounPossessive"] = "his",
        ["NECROBINDER.pronounSubject"] = "he",
        ["NECROBINDER.aromaPrinciple"] = "[sine][purple]The heart of the cards... I will not falter...[/purple][/sine]",
        ["NECROBINDER.goldMonologue"] = "[sine]Maybe I can finally buy a booster pack...[/sine]",
        ["NECROBINDER.eventDeathPrevention"] = "My deck isn't finished yet.",
        ["NECROBINDER.cardsModifierTitle"] = "Duel Monsters Cards",
        ["NECROBINDER.cardsModifierDescription"] = "Duel Monsters cards will now appear in rewards and shops.",
    };

    static readonly System.Text.RegularExpressions.Regex Word = new(@"(?<![\[/{A-Za-z_])(The Necrobinder|the Necrobinder|Necrobinder)(?![A-Za-z_:}\]])");

    public static void ApplyLoc(LocManager loc)
    {
        try
        {
            loc.GetTable("characters").MergeWith(Text);
            // every other mention of the Necrobinder by name
            var tables = AccessTools.FieldRefAccess<LocManager, Dictionary<string, LocTable>>("_tables")(loc);
            var tr = AccessTools.FieldRefAccess<LocTable, Dictionary<string, string>>("_translations");
            foreach (var table in tables.Values)
            {
                var d = tr(table);
                foreach (var key in d.Keys.ToList())
                    if (d[key] is { } v && v.Contains("Necrobinder") && !Text.ContainsKey(key)) d[key] = Word.Replace(v, Name);
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] duel master k text: " + ex.Message); }
    }

    static Texture2D? _portrait, _round;
    static bool _tried;

    public static Texture2D? Portrait()
    {
        if (_tried) return _portrait;
        _tried = true;
        try
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmDuelMasterK).Assembly.Location) ?? ".", "duel_master_k.png");
            if (!System.IO.File.Exists(file)) return null;
            var img = Image.LoadFromFile(file);
            if (img == null) return null;
            _portrait = ImageTexture.CreateFromImage(img);
            // rounded version for small icons
            var r = (Image)img.Duplicate(); r.Convert(Image.Format.Rgba8); r.Resize(128, 128, Image.Interpolation.Lanczos);
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                float dx = Math.Max(0, Math.Max(20 - x, x - 107)), dy = Math.Max(0, Math.Max(20 - y, y - 107));
                float a = Mathf.Clamp(20.5f - Mathf.Sqrt(dx * dx + dy * dy), 0f, 1f);
                if (dx > 0 && dy > 0) { var c = r.GetPixel(x, y); c.A = a; r.SetPixel(x, y, c); }
            }
            _round = ImageTexture.CreateFromImage(r);
            Log.Info("[FMDuelist] Duel Master K portrait loaded");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] k portrait: " + ex.Message); }
        return _portrait;
    }

    public static Texture2D? Icon() => Portrait() != null ? _round : null;
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.IconTexture), MethodType.Getter)]
static class KIconPatch
{
    static bool Prefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (__instance is not Necrobinder || FmDuelMasterK.Icon() is not { } t) return true;
        __result = t;
        return false;
    }
}

[HarmonyPatch(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Init))]
static class KSelectButtonPatch
{
    static readonly AccessTools.FieldRef<NCharacterSelectButton, TextureRect> IconRef = AccessTools.FieldRefAccess<NCharacterSelectButton, TextureRect>("_icon");

    [HarmonyPriority(Priority.Low)]
    static void Postfix(NCharacterSelectButton __instance, CharacterModel character)
    {
        try
        {
            if (character is Necrobinder && FmDuelMasterK.Icon() is { } t && IconRef(__instance) is { } icon) icon.Texture = t;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] k button: " + ex.Message); }
    }
}

/// <summary>Character-select art: K's portrait, large, framed, over a dark backdrop.</summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
static class KSelectArtPatch
{
    static void Postfix(NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        try
        {
            if (characterModel is not Necrobinder || FmDuelMasterK.Portrait() is not { } tex) return;
            var bg = __instance.GetNodeOrNull<Control>("AnimatedBg")?.GetNodeOrNull<Control>(characterModel.Id.Entry + "_bg");
            if (bg == null) return;
            var back = new ColorRect { Color = new Color(0.07f, 0.05f, 0.09f), MouseFilter = Control.MouseFilterEnum.Ignore };
            back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            bg.AddChild(back);
            var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.1f, 0.08f, 0.05f), BorderColor = new Color(0.95f, 0.78f, 0.3f),
                BorderWidthLeft = 8, BorderWidthRight = 8, BorderWidthTop = 8, BorderWidthBottom = 8,
                ShadowColor = new Color(1f, 0.8f, 0.3f, 0.35f), ShadowSize = 40,
            });
            // placed by screen position (the background node is wider than the screen) but kept a normal child,
            // so it fades out with the rest of the screen
            var vp = __instance.GetViewportRect().Size;
            float h = vp.Y * 0.64f, w = h * tex.GetWidth() / tex.GetHeight();
            var place = Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(frame) || !frame.IsInsideTree()) return;
                var inv = bg.GetGlobalTransform().AffineInverse();
                var tl = inv * new Vector2(vp.X * 0.465f, vp.Y * 0.09f);
                var br = inv * new Vector2(vp.X * 0.465f + w, vp.Y * 0.09f + h);
                frame.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                frame.Position = tl;
                frame.Size = br - tl;
            });
            var art = new TextureRect
            {
                Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            frame.AddChild(art);
            bg.AddChild(frame);
            place.CallDeferred();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] k art: " + ex.Message); }
    }
}
