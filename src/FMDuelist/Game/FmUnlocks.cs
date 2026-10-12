using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;

namespace FMDuelist.Game;

/// <summary>
/// Nothing in the menus is ever locked: every Timeline piece is revealed (read), and every relic, card, potion and
/// event is marked as seen, so the main menu never forces a Timeline visit or greys out Singleplayer.
/// </summary>
public static class FmUnlocks
{
    static bool _done;

    public static void UnlockAll()
    {
        if (_done) return;
        _done = true;
        try
        {
            var progress = SaveManager.Instance.Progress;
            int epochs = 0, seen = 0;
            foreach (var id in EpochModel.AllEpochIds)
                if (!progress.IsEpochRevealed(id)) { progress.ObtainEpochOverride(id, EpochState.Revealed); epochs++; }
            foreach (var r in ModelDb.AllRelics) if (progress.MarkRelicAsSeen(r.Id)) seen++;
            foreach (var c in ModelDb.AllCards) if (progress.MarkCardAsSeen(c.Id)) seen++;
            foreach (var p in ModelDb.AllPotions) if (progress.MarkPotionAsSeen(p.Id)) seen++;
            foreach (var e in ModelDb.AllEvents) if (progress.MarkEventAsSeen(e.Id)) seen++;
            if (epochs + seen > 0) SaveManager.Instance.SaveProgressFile();
            Log.Info($"[FMDuelist] Unlocked: {epochs} timeline pieces revealed, {seen} entries marked as seen");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] unlock all: " + ex.Message); }
    }
}

[HarmonyPatch]
static class MenuUnlockPatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
    static void Ready() => FmUnlocks.UnlockAll();

    /// <summary>The menu never locks you into the Timeline: every button stays usable.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(NMainMenu), "UpdateTimelineButtonBehavior")]
    static void Buttons(NMainMenu __instance)
    {
        try
        {
            var t = Traverse.Create(__instance);
            foreach (var name in new[] { "_singleplayerButton", "_multiplayerButton", "_compendiumButton", "_timelineButton" })
                if (t.Field(name).GetValue() is MegaCrit.Sts2.Core.Nodes.GodotExtensions.NButton b) b.Enable();
            if (t.Field("_timelineButton").GetValue() is Godot.CanvasItem tl) tl.Visible = true;
            if (t.Field("_timelineNotificationDot").GetValue() is Godot.CanvasItem dot) dot.Visible = false;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] menu buttons: " + ex.Message); }
    }

    /// <summary>The Timeline button opens the Forbidden Memories Library instead.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(NMainMenu), "OpenTimelineScreen")]
    static bool Library()
    {
        FmLibrary.Show();
        return false;
    }
}

/// <summary>The Compendium's card library shows only Forbidden Memories cards.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary.NCardLibraryGrid), nameof(MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary.NCardLibraryGrid._Ready))]
static class FmOnlyCardLibraryPatch
{
    static void Prefix(MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary.NCardLibraryGrid __instance)
    {
        // _Ready fills _allCards from every pool; keep only the FM duel cards (not the Attack/Defense/zone helpers)
        Godot.Callable.From(() =>
        {
            try
            {
                if (Traverse.Create(__instance).Field("_allCards").GetValue() is List<CardModel> all)
                {
                    all.RemoveAll(c => c is not Cards.FmCardBase || c is Cards.FmPositionCard || c is Cards.FmEmptyZone);
                    Log.Info($"[FMDuelist] Card library: {all.Count} Forbidden Memories cards");
                }
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] card library filter: " + ex.Message); }
        }).CallDeferred();
    }
}
