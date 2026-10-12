using FMDuelist.Cards;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace FMDuelist.Game;

public enum JunkKind { None, Harmless, Harmful, Step }

/// <summary>
/// Junk cards (statuses and curses) in FM runs. They can be marked as fusion material but never fuse:
/// harmless junk (Dazed, Slimed, Wound...) is knocked away and the monster carries on; junk that hurts you while it's
/// in your hand (Burn, Decay, Doubt...) takes the monster down with it: both are destroyed. Frantic Escape ("Step
/// Back") is summoned like a spell, uses your summon for the turn, and stacks only with itself: 1-5 steps.
/// </summary>
public static class FmJunk
{
    public const int MaxSteps = 5;

    public static JunkKind Kind(CardModel? c) => c switch
    {
        null or FmCardBase => JunkKind.None,
        FranticEscape => JunkKind.Step,
        _ when c.Type is CardType.Status or CardType.Curse => c.HasTurnEndInHandEffect ? JunkKind.Harmful : JunkKind.Harmless,
        _ => JunkKind.None,
    };

    /// <summary>Step Back: every marked Frantic Escape plus the played one, up to 5 steps.</summary>
    public static async Task StepBack(PlayerChoiceContext ctx, Player owner, CardModel played)
    {
        var hand = owner.PlayerCombatState!.Hand.Cards;
        var extra = FmField.Marked.Where(c => c != played && c is FranticEscape && hand.Contains(c)).Take(MaxSteps - 1).ToList();
        FmField.Marked.Clear();
        FmField.NotifyChanged();
        int steps = 1 + extra.Count;
        try { await FmFusionAnim.PlayStepBack(played, steps); } catch (Exception ex) { Log.Warn("[FMDuelist] step anim: " + ex.Message); }
        foreach (var c in extra) await CardPileCmd.Add(c, PileType.Discard);
        var worm = owner.Creature.CombatState?.Enemies.FirstOrDefault(e => e.HasPower<SandpitPower>());
        var pit = worm?.Powers.OfType<SandpitPower>().FirstOrDefault(s => s.Target == owner.Creature);
        if (pit != null) await PowerCmd.ModifyAmount(ctx, pit, steps, worm, played);
        FmField.SummonsThisTurn++;
        FmField.NotifyChanged();
        Log.Info($"[FMDuelist] Step Back x{steps}");
    }
}

[HarmonyPatch]
static class FranticEscapePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(FranticEscape), "OnPlay")]
    static bool Play(FranticEscape __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        if (__instance.Owner is not { } p || !FmRules.IsFmPlayer(p)) return true;
        __result = FmJunk.StepBack(choiceContext, p, __instance);
        return false;
    }

    /// <summary>Step Back uses your summon for the turn.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CardModel), nameof(CardModel.CanPlay), new[] { typeof(UnplayableReason), typeof(AbstractModel) },
        new[] { ArgumentType.Out, ArgumentType.Out })]
    static void NeedsSummon(CardModel __instance, ref bool __result)
    {
        if (__result && __instance is FranticEscape && __instance.Owner is { } p && FmRules.IsFmPlayer(p) && !FmField.CanSummon(p)) __result = false;
    }
}
