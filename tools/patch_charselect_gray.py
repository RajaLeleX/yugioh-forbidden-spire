import os, re
H = os.path.expanduser('~/fmsts')
p = f'{H}/src/FMDuelist/FmMod.cs'
s = open(p, encoding='utf-8').read()

# drop the hard locks (keep "everyone unlocked")
start = s.index('/// <summary>Only the FM duelist (Necrobinder) can be picked while the mod is on')
end = s.index('/// <summary>The character screen opens on the Necrobinder')
s = s[:start] + s[end:]

s = s.rstrip() + '''

/// <summary>Grayscale for characters the mod doesn't support (icon buttons and their big background art).</summary>
static class FmGray
{
    const string IconShader = @"shader_type canvas_item;
void fragment(){ vec4 c = texture(TEXTURE, UV) * COLOR; float g = dot(c.rgb, vec3(0.299, 0.587, 0.114)); COLOR = vec4(vec3(g) * 0.75, c.a); }";
    const string ScreenShader = @"shader_type canvas_item;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
void fragment(){ vec3 c = texture(screen_texture, SCREEN_UV).rgb; float g = dot(c, vec3(0.299, 0.587, 0.114)); COLOR = vec4(vec3(g) * 0.7, 1.0); }";
    static Shader? _icon, _screen;
    public static ShaderMaterial Icon() => new() { Shader = _icon ??= new Shader { Code = IconShader } };
    public static ShaderMaterial Screen() => new() { Shader = _screen ??= new Shader { Code = ScreenShader } };
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton), nameof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton.Init))]
static class CharacterIconGrayPatch
{
    static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton, TextureRect> Icon =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton, TextureRect>("_icon");

    static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton __instance, CharacterModel character)
    {
        try { if (character is not Necrobinder) Icon(__instance).Material = FmGray.Icon(); }
        catch (Exception ex) { Log.Warn("[FMDuelist] gray icon: " + ex.Message); }
    }
}

/// <summary>Selecting any other character grays out its art and disables the Embark check.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen.SelectCharacter))]
static class CharacterEmbarkGatePatch
{
    static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen, Control> Bg =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen, Control>("_bgContainer");
    static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen, MegaCrit.Sts2.Core.Nodes.CommonUi.NConfirmButton> Embark =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen, MegaCrit.Sts2.Core.Nodes.CommonUi.NConfirmButton>("_embarkButton");

    static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        try
        {
            if (characterModel is Necrobinder) { Embark(__instance).Enable(); Embark(__instance).Modulate = Colors.White; return; }
            Embark(__instance).Disable();
            Embark(__instance).Modulate = new Color(0.45f, 0.45f, 0.45f);
            var bg = Bg(__instance);
            var gray = new ColorRect { Material = FmGray.Screen(), MouseFilter = Control.MouseFilterEnum.Ignore, Name = "FMDuelistGray" };
            gray.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            bg.AddChild(gray);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] embark gate: " + ex.Message); }
    }
}
'''
open(p, 'w', encoding='utf-8').write(s + '\n')
print('ok')
