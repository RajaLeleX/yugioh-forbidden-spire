using FMDuelist.Cards;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.ValueProps;

namespace FMDuelist.Game;

/// <summary>
/// FM runs use Forbidden Memories cards only. Any base-game card an event, relic or potion would put into your deck,
/// hand or piles becomes an FM card instead: Strikes become a random healing spell, Defends a random trap, anything
/// else a random FM card. (Curses and statuses are left alone: they're penalties, not cards you'd play.)
/// </summary>
public static class FmNoNativeCards
{
    static readonly int[] Heals = { 338, 339, 340, 341 };
    static readonly int[] Traps = { 681, 682, 683, 684, 685, 686, 687, 688, 689, 690 };
    static readonly Random Rng = new();

    public static bool IsNative(CardModel c) => c is not FmCardBase && c.Type is CardType.Attack or CardType.Skill or CardType.Power;

    public static CardModel Replace(CardModel native, Player owner)
    {
        int id = native.Tags.Contains(CardTag.Strike) ? Heals[Rng.Next(Heals.Length)]
            : native.Tags.Contains(CardTag.Defend) ? Traps[Rng.Next(Traps.Length)]
            : RandomPoolCard();
        var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id)));
        var scope = native.CardScope ?? (ICardScopeHolder.Of(owner));
        var fm = scope.CreateCard(canonical, owner);
        Log.Info($"[FMDuelist] {native.Id.Entry} replaced with {Data.FmData.Get(id).Name}");
        return fm;
    }

    static int RandomPoolCard()
    {
        while (true)
        {
            var t = FmSheet.PoolTypes[Rng.Next(FmSheet.PoolTypes.Length)];
            if (int.TryParse(t.Name.Replace("FmCard", ""), out int id) && id is not (>= 17 and <= 21)) return id;
        }
    }
}

static class ICardScopeHolder
{
    public static MegaCrit.Sts2.Core.Runs.ICardScope Of(Player p) => (MegaCrit.Sts2.Core.Runs.ICardScope?)p.Creature.CombatState ?? p.RunState;
}

[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add), new[] { typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool) })]
static class NativeCardAddPatch
{
    static void Prefix(ref IEnumerable<CardModel> cards)
    {
        try
        {
            var list = cards.ToList();
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                // only brand-new cards (not ones moving between your piles)
                if (c.Owner is not { } owner || !FmRules.IsFmPlayer(owner) || !FmNoNativeCards.IsNative(c) || c.Pile != null) continue;
                list[i] = FmNoNativeCards.Replace(c, owner);
                changed = true;
            }
            if (changed) cards = list;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] native card swap: " + ex.Message); }
    }
}

/// <summary>Fortifier ("triple your Block"): +1000 DEF to one of your monsters until your next turn.</summary>
[HarmonyPatch(typeof(Fortifier), "OnUse")]
static class FortifierPatch
{
    static bool Prefix(Fortifier __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        if (__instance.Owner is not { } p || !FmRules.IsFmPlayer(p)) return true;
        __result = Use(choiceContext, p, __instance);
        return false;
    }

    static async Task Use(PlayerChoiceContext ctx, Player p, Fortifier potion)
    {
        var living = FmField.Living(p).ToList();
        if (living.Count == 0) return;
        FmSlot? slot = living.Count == 1 ? living[0] : await FmFusion.ChooseMonster(ctx, p, potion, living);
        if (slot == null) return;
        slot.TempDef += FmRules.TempDefBoost;
        FmField.NotifyChanged();
    }
}

/// <summary>Pael's Legion (doubles a card's Block): your monsters gain +1000 DEF until your next turn instead.</summary>
[HarmonyPatch]
static class PaelsLegionPatch
{
    [HarmonyPostfix, HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion), nameof(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion.ModifyBlockMultiplicative))]
    static void NoDouble(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion __instance, ref decimal __result)
    {
        if (__result > 1m && FmRules.IsFmPlayer(__instance.Owner)) __result = 1m;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion), nameof(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion.AfterCardPlayed))]
    static bool Boost(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion __instance, CardPlay cardPlay, ref Task __result)
    {
        if (!FmRules.IsFmPlayer(__instance.Owner)) return true;
        __result = Task.CompletedTask;
        var t = Traverse.Create(__instance);
        if (t.Property("Cooldown").GetValue<int>() > 0) return false;
        if (cardPlay.Card is not FmSpellCard { Kind: FmSpellKind.Block or FmSpellKind.BlockWeak or FmSpellKind.BlockCleanse or FmSpellKind.BlockHeal }) return false;
        var living = FmField.Living(__instance.Owner).ToList();
        if (living.Count == 0) return false;
        foreach (var s in living) s.TempDef += FmRules.TempDefBoost;
        FmField.NotifyChanged();
        t.Property("Cooldown").SetValue(__instance.DynamicVars["Turns"].IntValue);
        __instance.Status = MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Normal;
        return false;
    }
}

public static class FmNativeCardText
{
    public static void ApplyLoc(MegaCrit.Sts2.Core.Localization.LocManager loc)
    {
        try
        {
            loc.GetTable("potions").MergeWith(new Dictionary<string, string>
            {
                ["FORTIFIER.description"] = "Choose one of your monsters. It gains [blue]1000[/blue] DEF until your next turn.",
            });
            loc.GetTable("relics").MergeWith(new Dictionary<string, string>
            {
                ["PAELS_LEGION.description"] = "When you play a [gold]Block[/gold] spell, your monsters gain [blue]1000[/blue] DEF until your next turn, then this goes to sleep for [blue]{Turns}[/blue] {Turns:plural:turn|turns}.",
                ["LARGE_CAPSULE.description"] = "Upon pickup, obtain [blue]{Relics}[/blue] random {Relics:plural:Relic|Relics}. Add a random healing spell and a random trap card to your [gold]Deck[/gold].",
                ["LARGE_CAPSULE.eventDescription"] = "Obtain [blue]{Relics}[/blue] random {Relics:plural:Relic|Relics}. Add a random healing spell and a random trap card to your [gold]Deck[/gold].",
            });
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] native card text: " + ex.Message); }
    }
}
