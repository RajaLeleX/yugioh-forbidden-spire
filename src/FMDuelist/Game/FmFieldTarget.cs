using FMDuelist.Cards;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace FMDuelist.Game;

/// <summary>
/// A monster card's targeting arrow can also land on one of your own monsters on the field: the card then fuses onto it
/// (that field monster is the first piece of the fusion chain).
/// </summary>
public static class FmFieldTarget
{
    /// <summary>True while the arrow being dragged belongs to a monster card from the hand.</summary>
    public static bool MonsterArrow;
    /// <summary>The equip card whose arrow is being dragged (it can only land on monsters that can use it).</summary>
    public static Cards.FmSpellCard? EquipArrow;

    public static FmSlot? SlotOf(CardModel card, Creature? target) =>
        target == null || card.Owner == null ? null : FmField.Slots.FirstOrDefault(s => s.Alive(card.Owner) && s.Creature(card.Owner) == target);

    public static bool CanTakeEquip(Cards.FmSpellCard equip, Creature c) =>
        equip.Owner is { } p && FmField.Slots.Any(s => s.Alive(p) && s.Creature(p) == c && Data.FmData.CanEquip(equip.FmId, s.MonsterId));

    public static bool IsFieldMonster(Creature c) =>
        FmField.Duelist is { } p && FmField.Slots.Any(s => s.Alive(p) && s.Creature(p) == c);

    internal static void Begin(object cardPlay)
    {
        try
        {
            var card = Traverse.Create(cardPlay).Property("Card").GetValue();
            MonsterArrow = card is FmMonsterCard;
            EquipArrow = card is Cards.FmSpellCard { Kind: Cards.FmSpellKind.Equip } sp ? sp : null;
        }
        catch { MonsterArrow = false; EquipArrow = null; }
    }
}

[HarmonyPatch(typeof(NMouseCardPlay), "SingleCreatureTargeting")]
static class MouseMonsterArrowPatch { static void Prefix(NMouseCardPlay __instance) => FmFieldTarget.Begin(__instance); }

[HarmonyPatch(typeof(NControllerCardPlay), "SingleCreatureTargeting")]
static class ControllerMonsterArrowPatch { static void Prefix(NControllerCardPlay __instance) => FmFieldTarget.Begin(__instance); }

[HarmonyPatch(typeof(NTargetManager), "AllowedToTargetCreature")]
static class FieldMonsterTargetablePatch
{
    static void Postfix(Creature creature, ref bool __result)
    {
        if (FmFieldTarget.EquipArrow is { } eq) { __result = creature.IsAlive && FmFieldTarget.CanTakeEquip(eq, creature); return; }
        if (FmRival.Active && (FmRival.IsZoneCreature(creature) || creature == FmRival.Priest))
        {
            // your monsters' attack arrow picks his monsters (or him once his field is empty); cards aim at him
            __result = FmField.Aiming ? FmRival.CanBeAttacked(creature) : creature == FmRival.Priest;
            return;
        }
        if (!__result && FmFieldTarget.MonsterArrow && creature.IsAlive && FmFieldTarget.IsFieldMonster(creature)) __result = true;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
static class FieldMonsterValidTargetPatch
{
    static void Postfix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (__instance is Cards.FmSpellCard { Kind: Cards.FmSpellKind.Equip } eq) { __result = target != null && target.IsAlive && FmFieldTarget.CanTakeEquip(eq, target); return; }
        if (FmRival.IsZoneCreature(target)) { __result = false; return; }
        if (!__result && __instance is FmMonsterCard && target != null && target.IsAlive && FmFieldTarget.SlotOf(__instance, target) != null) __result = true;
    }
}
