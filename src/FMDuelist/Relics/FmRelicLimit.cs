using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using FMDuelist.Game;

namespace FMDuelist.Relics;

/// <summary>
/// A Forbidden Memories run has only FM's relics: the Millennium Puzzle and the six Millennium Items, the starter
/// relics (the run's and each act's opening ones, only in that act's starting room) and the Password Revealer's one-combat Drop Rate Boost. Shops sell one Millennium Item and no potions,
/// any other relic a room tries to give becomes a Millennium Item you don't have yet (or nothing once you have all
/// six), and Slay the Spire's own buffs and debuffs (Strength, Dexterity, Regen...) never land on you or your monsters.
/// </summary>
public static class FmRelicLimit
{
    static readonly Random _rng = new();

    /// <summary>Relics an FM run can get here: the Millennium Items and the Drop Rate Boost anywhere; the starter relics
    /// (the run's own and each act's opening Ancient relics) only in an act's starting room.</summary>
    public static bool Fm(RelicModel r, Player p) => r is FmMillenniumItem or MillenniumPuzzle or FmDropBoost
        || (p.RunState.ActFloor == 0 && (r is ExodiaCardBox or MagicProtectionKit or DragonTreasure or EquipBoosterPack
            // an act's opening relic only when the mod has made it a Forbidden Memories one (its art and name)
            || (r.Rarity is MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Starter or MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Ancient
                && (FmRetheme.RelicArt(r) != null || FmGuardianStars.RelicIcons.ContainsKey(r.Id.Entry)))));

    /// <summary>A random Millennium Item the player doesn't own yet (mutable), or null when they have all six.</summary>
    public static RelicModel? NextItem(Player p)
    {
        var left = FmMillenniumItems.All.Where(t => !p.Relics.Any(r => r.GetType() == t)).ToList();
        if (left.Count == 0) return null;
        return ModelDb.GetById<RelicModel>(ModelDb.GetId(left[_rng.Next(left.Count)])).ToMutable();
    }
}

[HarmonyPatch(typeof(MerchantInventory), "PopulateRelicEntries")]
static class FmShopOneItemPatch
{
    static bool Prefix(MerchantInventory __instance)
    {
        if (!FmRules.IsFmPlayer(__instance.Player)) return true;
        if (FmRelicLimit.NextItem(__instance.Player) is { } item) __instance.AddRelicEntry(new MerchantRelicEntry(item, __instance.Player));
        return false;
    }
}

[HarmonyPatch(typeof(MerchantInventory), "PopulatePotionEntries")]
static class FmShopNoPotionsPatch
{
    static bool Prefix(MerchantInventory __instance) => !FmRules.IsFmPlayer(__instance.Player);
}

/// <summary>The shop's empty relic and potion slots are hidden.</summary>
[HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Initialize))]
static class FmShopHideEmptyPatch
{
    static void Postfix(NMerchantInventory __instance, MerchantInventory inventory)
    {
        try
        {
            if (!FmRules.IsFmPlayer(inventory.Player)) return;
            if (Traverse.Create(__instance).Field("_relicContainer").GetValue() is Node relics)
                for (int i = 0; i < relics.GetChildCount(); i++)
                    if (relics.GetChild(i) is CanvasItem c) c.Visible = i < inventory.RelicEntries.Count;
            if (Traverse.Create(__instance).Field("_potionContainer").GetValue() is CanvasItem potions) potions.Visible = false;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] shop slots: " + ex.Message); }
    }
}

/// <summary>Any relic that isn't FM's becomes a Millennium Item you don't have (or nothing).</summary>
[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
static class FmRelicObtainPatch
{
    static bool Prefix(ref RelicModel relic, Player player, ref Task<RelicModel> __result)
    {
        if (!FmRules.IsFmPlayer(player) || FmRelicLimit.Fm(relic, player)) return true;
        if (FmRelicLimit.NextItem(player) is { } item)
        {
            Log.Info($"[FMDuelist] relic {relic.Id.Entry} -> {item.Id.Entry}");
            relic = item;
            return true;
        }
        Log.Info($"[FMDuelist] relic {relic.Id.Entry} skipped (all Millennium Items owned)");
        __result = Task.FromResult(relic);
        return false;
    }
}

/// <summary>No Slay the Spire powers on the FM player or their monsters (the mod's own powers still apply).</summary>
[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply), new[] { typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool) })]
static class FmNoStsPowersPatch
{
    static bool Prefix(PowerModel power, Creature target, ref Task __result)
    {
        var owner = target.Player ?? target.PetOwner;
        if (owner == null || !FmRules.IsFmPlayer(owner) || power.GetType().Assembly == typeof(FmRelicLimit).Assembly) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>No potions in an FM run, from any source.</summary>
[HarmonyPatch(typeof(PotionCmd), nameof(PotionCmd.TryToProcure), new[] { typeof(PotionModel), typeof(Player), typeof(int) })]
static class FmNoPotionsPatch
{
    static bool Prefix(Player player, ref Task<MegaCrit.Sts2.Core.Entities.Potions.PotionProcureResult> __result)
    {
        if (!FmRules.IsFmPlayer(player)) return true;
        __result = Task.FromResult(new MegaCrit.Sts2.Core.Entities.Potions.PotionProcureResult { success = false });
        return false;
    }
}
