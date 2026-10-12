using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace FMDuelist.Game;

/// <summary>
/// The game's run music is stopped while FM music plays; its later track/parameter updates then hit a stopped FMOD event
/// and throw from native code, which kills the process. Those calls are skipped while FM music plays and never allowed
/// to throw.
/// </summary>
[HarmonyPatch]
static class RunMusicGuardPatch
{
    static IEnumerable<MethodBase> TargetMethods() =>
        typeof(NRunMusicController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            // every music/ambience call the game makes on its controller (not Godot's own overrides)
            .Where(m => !m.Name.StartsWith("_") && m.Name != "SetRunState" && !m.IsSpecialName
                && m.DeclaringType == typeof(NRunMusicController) && !m.Name.Contains("Godot"))
            .Where(m => !m.IsAbstract && !m.ContainsGenericParameters);

    static bool Prefix(MethodBase __originalMethod) =>
        !(FmMusic.IsPlaying && __originalMethod.Name is "UpdateTrack" or "UpdateMusicParameter" or "ToggleMerchantTrack" or "TriggerEliteSecondPhase"
            or "UpdateCustomTrack" or "TriggerCampfireGoingOut");

    static Exception? Finalizer(Exception? __exception, MethodBase __originalMethod)
    {
        if (__exception != null) Log.Warn($"[FMDuelist] game music call {__originalMethod.Name} failed (ignored): {__exception.Message}");
        return null;
    }
}
