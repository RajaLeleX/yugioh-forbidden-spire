using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using FMDuelist.Game;

namespace FMDuelist.Relics;

/// <summary>
/// Drop Rate Boost: the bonus for buying a card through the Password Revealer. For the next combat only, card
/// rewards are 5% more likely to be Uncommon and 5% more likely to be Rare (Common gives up the 10%). It is armed
/// when that combat starts and leaves once you walk into the next room.
/// </summary>
public sealed class FmDropBoost : RelicModel
{
    public const float Boost = 0.05f;
    public override RelicRarity Rarity => RelicRarity.Event;
    public override bool IsAllowed(IRunState runState) => false; // never offered; only the Password Revealer gives it

    /// <summary>True once its combat has begun (saved, so a reload mid-fight keeps the boost and still removes it after).</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public bool Armed { get; set; }

    /// <summary>Its one combat is over (a second combat before leaving, like another Free Duel, gets no boost).</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public bool Spent { get; set; }

    public override Task BeforeCombatStart()
    {
        if (Armed) Spent = true;
        else { Armed = true; Flash(); }
        return Task.CompletedTask;
    }

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (Armed && room is not CombatRoom)
            Godot.Callable.From(() => MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(RelicCmd.Remove(this))).CallDeferred();
        return Task.CompletedTask;
    }

    /// <summary>Is a boost in effect for this run's combat rewards?</summary>
    public static bool Active =>
        RunManager.Instance.DebugOnlyGetState()?.Players.Any(p => p.GetRelic<FmDropBoost>() is { Armed: true, Spent: false }) ?? false;

    /// <summary>Gives the player a fresh boost (or keeps the one waiting), for the next combat.</summary>
    public static async Task Grant(Player player)
    {
        if (player.GetRelic<FmDropBoost>() is { } have) { have.Armed = have.Spent = false; return; }
        await RelicCmd.Obtain(ModelDb.Relic<FmDropBoost>().ToMutable(), player);
    }

    public const string Title = "Drop Rate Boost";
    public const string Text = "For your next combat only, card rewards are [blue]5%[/blue] more likely to be Uncommon and [blue]5%[/blue] more likely to be Rare. Free Duel drops get the same edge.";
    public const string Flavor = "A password well spent.";
}

/// <summary>The boost itself: shifts 5% from Common to Uncommon and 5% to Rare in the game's card reward roll.</summary>
[HarmonyPatch(typeof(CardRarityOdds), nameof(CardRarityOdds.RollWithoutChangingFutureOdds), new[] { typeof(CardRarityOddsType), typeof(float) })]
static class FmDropBoostOddsPatch
{
    static void Prefix(CardRarityOddsType type, ref float offset, out float __state)
    {
        __state = 0;
        if (type is CardRarityOddsType.BossEncounter or CardRarityOddsType.Uniform || !FmDropBoost.Active) return;
        // rare chance + 5%: the uncommon band sits on top of the rare one, so it moves up with it and keeps its size
        float rare = type switch { CardRarityOddsType.EliteEncounter => CardRarityOdds.EliteRareOdds, CardRarityOddsType.Shop => CardRarityOdds.ShopRareOdds, _ => CardRarityOdds.RegularRareOdds };
        float uncommon = type == CardRarityOddsType.EliteEncounter ? CardRarityOdds.eliteUncommonOdds : CardRarityOdds.regularUncommonOdds;
        offset += FmDropBoost.Boost;
        // then enough Commons turn Uncommon to add 5% there as well
        float common = 1f - Math.Max(0f, rare + offset) - uncommon;
        __state = common > FmDropBoost.Boost ? FmDropBoost.Boost / common : 1f;
    }

    static void Postfix(ref CardRarity __result, float __state)
    {
        if (__state > 0 && __result == CardRarity.Common && FmDropBoostRng.Next() < __state) __result = CardRarity.Uncommon;
    }
}

static class FmDropBoostRng
{
    static readonly Random _rng = new();
    public static float Next() => (float)_rng.NextDouble();
}
