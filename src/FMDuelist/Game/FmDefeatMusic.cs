using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;

namespace FMDuelist.Game;

/// <summary>Losing a run plays FM's "You Lose" music (once) on the game over screen.</summary>
[HarmonyPatch(typeof(NGameOverScreen), nameof(NGameOverScreen._Ready))]
static class DefeatMusicPatch
{
    static void Postfix()
    {
        try
        {
            if (!FmRules.RunActive || FmField.Duelist is not { } p || p.Creature.IsAlive) return;
            FmMusic.PlayOnceThen(FmMedia.DefeatTrack, FmMedia.GameOverTrack, FmMedia.GameOverGap);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] defeat music: " + ex.Message); }
    }
}

/// <summary>When the map opens to travel (not a peek from the top bar), the act's map theme plays.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Open))]
static class MapMusicPatch
{
    static void Postfix(bool isOpenedFromTopBar)
    {
        try
        {
            if (isOpenedFromTopBar || !FmRules.RunActive) return;
            int a = FmRules.ActIndex;
            if (a >= 0 && a < FmMedia.ActMapTracks.Length && FmMedia.ActMapTracks[a] >= 0) FmMusic.Play(FmMedia.ActMapTracks[a]);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] map music: " + ex.Message); }
    }
}
