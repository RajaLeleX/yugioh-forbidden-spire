import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:80]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# a public way to clear the duel commentary
patch('Game/FmRival.cs', [
('''    static CanvasLayer? _bannerLayer;''', '''    static CanvasLayer? _bannerLayer;

    /// <summary>Clears the commentary line at once (leaving a run).</summary>
    public static void ClearBanner()
    {
        if (_bannerLayer != null && GodotObject.IsInstanceValid(_bannerLayer)) _bannerLayer.QueueFree();
        _bannerLayer = null; _bannerLabel = null;
    }'''),
])

patch('Game/FmPractice.cs', [
# the loser falls (death animation) before the results
('''    public static async Task Finish(bool won, MegaCrit.Sts2.Core.Entities.Players.Player player, int comLp, int turns)
    {''', '''    public static async Task Finish(bool won, MegaCrit.Sts2.Core.Entities.Players.Player player, int comLp, int turns)
    {
        try
        {
            var loser = won ? FmRival.Priest : player.Creature;
            if (loser != null) await CreatureCmd.TriggerAnim(loser, "Dead", 1.4f);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] practice death anim: " + ex.Message); }'''),
('''/// <summary>The Compendium button becomes "Duel the Duel Master K".</summary>''', '''/// <summary>
/// Giving up in the Free Duel counts as losing it: your Duel Master K falls, "YOU LOSE" and the results play,
/// then it's back to the main menu (instead of the run just fading out).
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.Abandon))]
static class FmPracticeGiveUpPatch
{
    static bool Prefix()
    {
        if (!FmPractice.Active) return true;
        var state = RunManager.Instance.DebugOnlyGetState();
        var player = state?.Players.FirstOrDefault();
        if (player == null) return true;
        try { MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance?.Close(); } catch { }
        int turns = player.Creature.CombatState?.RoundNumber ?? 1;
        int comLp = Math.Max(0, FmRival.Priest?.CurrentHp ?? 0);
        FmRival.Active = false;
        TaskHelper.RunSafely(FmPractice.Finish(false, player, comLp, turns));
        return false;
    }
}

/// <summary>
/// Leaving a run by any route (give up, death, quitting to the menu) clears every duel overlay the mod puts on
/// screen: his hand and set traps, the YOU/COM tags, the commentary line, your field row.
/// </summary>
[HarmonyPatch]
static class FmLeaveRunCleanupPatch
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (var n in new[] { nameof(NGame.ReturnToMainMenu), nameof(NGame.ReturnToMainMenuAfterRun), nameof(NGame.GoToTimelineAfterRun) })
            if (AccessTools.Method(typeof(NGame), n) is { } m) yield return m;
    }

    static void Prefix()
    {
        try
        {
            FmRival.Reset();
            FmRival.ClearBanner();
            FmPractice.RemoveLabels();
            FmField.ResetCombat();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] leave-run cleanup: " + ex.Message); }
    }
}

/// <summary>The Compendium button becomes "Duel the Duel Master K".</summary>'''),
])
print('ok')
