using System.Reflection;
using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Game;
using FMDuelist.Powers;
using FMDuelist.Relics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace FMDuelist;

[ModInitializer(nameof(Init))]
public static class FmMod
{
    public const string Id = "FMDuelist";
    public static Harmony Harmony = null!;

    /// <summary>
    /// The game finds [SavedProperty] values only on its own model classes (a fixed list), so the Millennium
    /// Puzzle's story flags were never written and a reloaded run forgot it was a story run. Add the mod's classes.
    /// </summary>
    static void RegisterSavedProperties()
    {
        try
        {
            var cache = typeof(MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(cache.TypeHandle);
            var add = HarmonyLib.AccessTools.Method(cache, "CachePropertiesForType");
            int n = 0;
            foreach (var t in typeof(FmMod).Assembly.GetTypes())
                if (!t.IsAbstract && typeof(MegaCrit.Sts2.Core.Models.AbstractModel).IsAssignableFrom(t)
                    && t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                        .Any(p => p.IsDefined(typeof(MegaCrit.Sts2.Core.Saves.Runs.SavedPropertyAttribute), true)))
                { add.Invoke(null, new object[] { t }); n++; }
            var names = (System.Collections.IList)HarmonyLib.AccessTools.Field(cache, "_netIdToPropertyNameMap").GetValue(null)!;
            HarmonyLib.AccessTools.Property(cache, "NetIdBitSize").SetValue(null, (int)Math.Ceiling(Math.Log2(Math.Max(2, names.Count))));
            Log.Info($"[FMDuelist] Saved properties registered for {n} mod classes");
        }
        catch (Exception ex) { Log.Error("[FMDuelist] saved properties: " + ex); }
    }

    public static void Init()
    {
        Log.Info("[FMDuelist] Init");
        RegisterSavedProperties();
        FmData.Load();
        Game.FmPractice.Preload();
        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.NecrobinderRelicPool, MillenniumPuzzle>();
        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, DragonTreasure>();
        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, MagicProtectionKit>();
        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, EquipBoosterPack>();
        ModHelper.AddModelToPool<MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool, ExodiaCardBox>();
        foreach (var t in FmRelicSheet.All) ModHelper.AddModelToPool(typeof(MegaCrit.Sts2.Core.Models.RelicPools.NecrobinderRelicPool), t);
        foreach (var t in FmMillenniumItems.All) ModHelper.AddModelToPool(typeof(MegaCrit.Sts2.Core.Models.RelicPools.NecrobinderRelicPool), t);
        FmAudio.StartBackgroundDecode();
        FmIntro.Load();
        Game.FmStarChips.Load();
        Harmony = new Harmony("fmduelist.mod");
        // patch class by class so one broken patch can't take the rest down with it
        foreach (var t in typeof(FmMod).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
            try { Harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception ex) { Log.Error($"[FMDuelist] patch {t.Name} failed: {ex.GetBaseException().Message}"); }
        }
        ModHelper.SubscribeForRunStateHooks("FMDuelist", rs => { FmRules.RunActive = rs.Players.Any(FmRules.IsFmPlayer); return Array.Empty<AbstractModel>(); });
        FmTest.Start();
    }
}

// ---------------- Necrobinder becomes the FM duelist ----------------
[HarmonyPatch(typeof(Necrobinder))]
static class NecrobinderPatches
{
    [HarmonyPostfix, HarmonyPatch(nameof(Necrobinder.StartingHp), MethodType.Getter)]
    static void Hp(ref int __result) => __result = FmRules.LifePoints;

    [HarmonyPostfix, HarmonyPatch(nameof(Necrobinder.StartingDeck), MethodType.Getter)]
    static void Deck(ref IEnumerable<CardModel> __result)
    {
        var deck = FmSheet.StarterTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t))).ToList();
        // a fresh set of weak Common monsters every run: one per type first (for fusion variety), then any
        var rng = Random.Shared;
        var byType = FmSheet.StarterMonsterPool.GroupBy(id => FmData.Get(id).Type).Select(g => g.OrderBy(_ => rng.Next()).ToList()).OrderBy(_ => rng.Next()).ToList();
        var picks = byType.Select(g => g[0]).Take(FmSheet.StarterMonsterCount).ToList();
        foreach (var id in FmSheet.StarterMonsterPool.Except(picks).OrderBy(_ => rng.Next()))
        {
            if (picks.Count >= FmSheet.StarterMonsterCount) break;
            picks.Add(id);
        }
        deck.InsertRange(0, picks.Select(id => ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id)))));
        foreach (var (count, ids) in FmSheet.StarterRandom)
            foreach (var id in ids.OrderBy(_ => Random.Shared.Next()).Take(count))
                deck.Add(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))));
        __result = deck;
    }

    [HarmonyPostfix, HarmonyPatch(nameof(Necrobinder.CardPool), MethodType.Getter)]
    static void Pool(ref CardPoolModel __result) => __result = ModelDb.CardPool<FmCardPool>();

    [HarmonyPostfix, HarmonyPatch(nameof(Necrobinder.StartingRelics), MethodType.Getter)]
    static void Relics(ref IReadOnlyList<RelicModel> __result) => __result = new List<RelicModel> { ModelDb.Relic<MillenniumPuzzle>() };
}

// ---------------- art from the disc ----------------
public static class FmArt
{
    private static readonly Dictionary<int, Texture2D> _sideways = new();

    public static Texture2D? Sideways(int id)
    {
        if (_sideways.TryGetValue(id, out var t)) return t;
        var src = FmData.Art(id);
        if (src == null) return null;
        var img = src.GetImage();
        img.Rotate90(ClockDirection.Counterclockwise);
        t = ImageTexture.CreateFromImage(img);
        _sideways[id] = t;
        return t;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Portrait), MethodType.Getter)]
static class PortraitPatch
{
    static bool Prefix(CardModel __instance, ref Texture2D __result)
    {
        if (__instance is not FmCardBase fm) return true;
        var tex = fm is FmAttackPosition ? Relics.FmPositionChoiceLook.Tex("position_attack") ?? FmData.Art(fm.ArtId)
            : fm is FmDefensePosition ? Relics.FmPositionChoiceLook.Tex("position_defense") ?? FmArt.Sideways(fm.ArtId)
            : FmData.Art(fm.ArtId);
        if (tex == null) return true;
        __result = tex;
        return false;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.PortraitPath), MethodType.Getter)]
static class PortraitPathPatch
{
    static readonly string Safe = MegaCrit.Sts2.Core.Helpers.ImageHelper.GetImagePath("atlases/card_atlas.sprites/necrobinder/poke.tres");
    static bool Prefix(CardModel __instance, ref string __result)
    {
        if (__instance is not FmCardBase) return true;
        __result = Safe;
        return false;
    }
}

[HarmonyPatch]
static class RelicIconPatches
{
    static int? ArtFor(RelicModel r) => FmGuardianStars.RelicIcons.ContainsKey(r.Id.Entry) ? 1 : r switch { MillenniumPuzzle => 362, DragonTreasure => 4, FmMillenniumItem m => m.ArtId, MagicProtectionKit => 348, EquipBoosterPack => 657, ExodiaCardBox => 21, FmRelic f => f.ArtId, _ => Game.FmRetheme.RelicArt(r) };

    [HarmonyPrefix, HarmonyPatch(typeof(RelicModel), nameof(RelicModel.Icon), MethodType.Getter)]
    static bool Icon(RelicModel __instance, ref Texture2D __result) => Swap(__instance, ref __result);

    [HarmonyPrefix, HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IconOutline), MethodType.Getter)]
    static bool Outline(RelicModel __instance, ref Texture2D __result) => Swap(__instance, ref __result);

    [HarmonyPrefix, HarmonyPatch(typeof(RelicModel), nameof(RelicModel.BigIcon), MethodType.Getter)]
    static bool Big(RelicModel __instance, ref Texture2D __result) => Swap(__instance, ref __result);

    static Texture2D? _puzzle;
    static Texture2D? PuzzleIcon()
    {
        if (_puzzle != null) return _puzzle;
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmMod).Assembly.Location) ?? ".", "millennium_puzzle.png");
        if (!System.IO.File.Exists(path)) return null;
        var img = Image.LoadFromFile(path);
        return _puzzle = img == null ? null : ImageTexture.CreateFromImage(img);
    }

    static bool Swap(RelicModel r, ref Texture2D result)
    {
        if (r is MillenniumPuzzle && PuzzleIcon() is { } pz) { result = pz; return false; }
        // Joey's Trump Card shows Joey (Jono, his FM counterpart) when his portrait is in the local story folder
        if (r is DragonTreasure && Game.FmScene.Tex("icon_jono") is { } joey) { result = joey; return false; }
        if (FmGuardianStars.RelicIcon(r) is { } star) { result = star; return false; }
        if (ArtFor(r) is not int id || FmData.Art(id) is not { } t) return true;
        result = t;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(RelicModel), nameof(RelicModel.PackedIconPath), MethodType.Getter)]
    static bool Path(RelicModel __instance, ref string __result)
    {
        if (ArtFor(__instance) == null) return true;
        __result = ModelDb.Relic<BoundPhylactery>().PackedIconPath;
        return false;
    }
}

[HarmonyPatch]
static class PowerIconPatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Icon), MethodType.Getter)]
    static bool Icon(PowerModel __instance, ref Texture2D __result) => Swap(__instance, ref __result);

    [HarmonyPrefix, HarmonyPatch(typeof(PowerModel), nameof(PowerModel.BigIcon), MethodType.Getter)]
    static bool Big(PowerModel __instance, ref Texture2D __result) => Swap(__instance, ref __result);

    static bool Swap(PowerModel p, ref Texture2D result)
    {
        if (p is not FmPower fp || FmData.Art(fp.IconCardId) is not { } t) return true;
        result = t;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PowerModel), nameof(PowerModel.PackedIconPath), MethodType.Getter)]
    static bool Path(PowerModel __instance, ref string __result)
    {
        if (__instance is not FmPower) return true;
        __result = ModelDb.Power<StrengthPower>().PackedIconPath;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PowerModel), nameof(PowerModel.ResolvedBigIconPath), MethodType.Getter)]
    static bool BigPath(PowerModel __instance, ref string __result)
    {
        if (__instance is not FmPower) return true;
        __result = ModelDb.Power<StrengthPower>().ResolvedBigIconPath;
        return false;
    }
}

// ---------------- Neow offers the Dragon buff ----------------
[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
static class NeowPatch
{
    static readonly MethodInfo RelicOption = AccessTools.Method(typeof(AncientEventModel), "RelicOption", new[] { typeof(RelicModel), typeof(string), typeof(string) });

    // Neow's own choices are skipped in FM runs (it would look for relics an FM run doesn't offer, and fail)
    static bool Prefix(Neow __instance, ref IReadOnlyList<EventOption> __result)
    {
        try
        {
            if (!FmRules.IsFmPlayer(__instance.Owner)) return true;
            var opt = (EventOption)RelicOption.Invoke(__instance, new object?[] { ModelDb.Relic<DragonTreasure>().ToMutable(), "INITIAL", "NEOW.pages.DONE.POSITIVE.description" })!;
            var exo = (EventOption)RelicOption.Invoke(__instance, new object?[] { ModelDb.Relic<ExodiaCardBox>().ToMutable(), "INITIAL", "NEOW.pages.DONE.POSITIVE.description" })!;
            // 3 choices; Dragon Treasure (35%) and the Exodia Card Box (15%) roll independently and replace normal choices
            // always the same three starters: the Exodia Card Box, the Magic Protection Kit and Joey's Trump Card
            var kit = (EventOption)RelicOption.Invoke(__instance, new object?[] { ModelDb.Relic<MagicProtectionKit>().ToMutable(), "INITIAL", "NEOW.pages.DONE.POSITIVE.description" })!;
            __result = new List<EventOption> { exo, kit, opt };
            Log.Info("[FMDuelist] Neow: the three FM starters");
            return false;
        }
        catch (Exception ex) { Log.Error("[FMDuelist] Neow option: " + ex); return true; }
    }
}

// ---------------- names and text from the disc ----------------
[HarmonyPatch(typeof(LocManager), "SetLanguageInternal")]
static class LocPatch
{
    static void Postfix(LocManager __instance) { FmLoc.Inject(__instance); Game.FmRetheme.ApplyLoc(__instance); Game.FmDuelMasterK.ApplyLoc(__instance); Game.FmNativeCardText.ApplyLoc(__instance); Game.FmGuardianStars.ApplyLoc(__instance); Game.FmShadow.ApplyLoc(__instance); Game.FmStatRework.ApplyLoc(__instance); Game.FmTransformPlus.ApplyLoc(__instance); Game.FmPlating.ApplyLoc(__instance); Game.FmRival.ApplyLoc(__instance); __instance.GetTable("main_menu_ui").MergeWith(new Dictionary<string, string> { ["TIMELINE"] = "Library", ["COMPENDIUM"] = "Duel the Duel Master K" }); __instance.GetTable("monsters").MergeWith(new Dictionary<string, string> { [ModelDb.GetEntry(typeof(Game.FmMirrorDK)) + ".name"] = "Duel Master K", [ModelDb.GetEntry(typeof(Game.FmMirrorDK)) + ".moves.FM_DUEL_MOVE.title"] = "Duel", [ModelDb.GetEntry(typeof(Game.FmHeishin)) + ".name"] = "High Mage Heishin", [ModelDb.GetEntry(typeof(Game.FmDuelSimon)) + ".name"] = "Simon Muran", [ModelDb.GetEntry(typeof(Game.FmDuelTeana)) + ".name"] = "Teana", [ModelDb.GetEntry(typeof(Game.FmDuelJono)) + ".name"] = "Jono", [ModelDb.GetEntry(typeof(Game.FmDuelVillager1)) + ".name"] = "Villager 1", [ModelDb.GetEntry(typeof(Game.FmDuelVillager2)) + ".name"] = "Villager 2", [ModelDb.GetEntry(typeof(Game.FmDuelVillager3)) + ".name"] = "Villager 3", [ModelDb.GetEntry(typeof(Game.FmHeishin)) + ".moves.FM_DUEL_MOVE.title"] = "Duel" }); __instance.GetTable("events").MergeWith(new Dictionary<string, string> { [ModelDb.GetEntry(typeof(Game.FmFestivalEvent)) + ".title"] = "The Festival", [ModelDb.GetEntry(typeof(Game.FmFestivalEvent)) + ".pages.INITIAL.description"] = "The mages' procession has passed through the town plaza. Priest Seto will be waiting at the duel ground...", [ModelDb.GetEntry(typeof(Game.FmFestivalEvent)) + ".pages.INITIAL.options.CONTINUE.title"] = "Continue", [ModelDb.GetEntry(typeof(Game.FmFestivalEvent)) + ".pages.INITIAL.options.CONTINUE.description"] = "One of the mages presses a gift into your hand...", [ModelDb.GetEntry(typeof(Game.FmFestivalEvent)) + ".pages.DONE.description"] = "The festival fades behind you. Priest Seto will be waiting at the duel ground..." }); __instance.GetTable("encounters").MergeWith(new Dictionary<string, string> { [ModelDb.GetEntry(typeof(Game.FmPracticeEncounter)) + ".title"] = "Duel the Duel Master K", [ModelDb.GetEntry(typeof(Game.FmPracticeEncounter)) + ".loss"] = "Duel Master K" }); __instance.GetTable("main_menu_ui").MergeWith(new Dictionary<string, string> { ["STANDARD.description"] = Game.StandardModePatch.Description }); Game.FmStarChips.RenameCurrency(__instance); }
}

// ---------------- the FM duelist (Necrobinder) is always unlocked ----------------
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), nameof(MegaCrit.Sts2.Core.Unlocks.UnlockState.Characters), MethodType.Getter)]
static class UnlockPatch
{
    // every character counts as unlocked, so a fresh save never runs unlock reveals
    static void Postfix(ref IEnumerable<CharacterModel> __result) => __result = ModelDb.AllCharacters.ToList();
}

/// <summary>The character screen opens on the Necrobinder (the only character the mod supports).</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen.OnSubmenuOpened))]
static class CharacterSelectDefaultPatch
{
    static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen __instance)
    {
        try
        {
            foreach (var b in __instance.FindChildren("*", "", true, false).OfType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton>())
                if (b.Name.ToString().StartsWith(ModelDb.Character<Necrobinder>().Id.Entry, StringComparison.OrdinalIgnoreCase)) { b.Select(); break; }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] select necrobinder: " + ex.Message); }
    }
}

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


/// <summary>Embark (the green check, its hotkey or controller button) only starts a run as the Necrobinder.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), "OnEmbarkPressed")]
static class EmbarkOnlyNecrobinderPatch
{
    static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen, MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton?> Selected =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen, MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton?>("_selectedButton");

    static bool Prefix(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen __instance) =>
        Selected(__instance) is { IsRandom: false } b && b.Character is Necrobinder;
}
