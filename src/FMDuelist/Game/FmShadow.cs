using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using FMDuelist.Powers;

namespace FMDuelist.Game;

/// <summary>
/// No Doom in FM runs. The items that dealt in Doom instead cast a Shadow Spell: the enemy loses a flat amount of
/// ATK and DEF for the rest of the combat, by rarity (Common 500, Uncommon 750, Rare and above 1000).
///   Potion of Doom (Common potion): one enemy loses 500 ATK/DEF.
///   Book Repair Knife (Uncommon relic): at the start of each combat, the strongest enemy loses 750 ATK/DEF.
///   Undying Sigil (Shop relic): at the start of each combat, ALL enemies lose 1000 ATK/DEF.
/// On a boss's monster zone the loss goes straight onto that monster card's ATK/DEF.
/// </summary>
public static class FmShadow
{
    public static int ForRarity(RelicRarity r) => r switch { RelicRarity.Common => 500, RelicRarity.Uncommon => 750, _ => 1000 };
    public static int ForRarity(PotionRarity r) => r switch { PotionRarity.Common => 500, PotionRarity.Uncommon => 750, _ => 1000 };

    public static int Potion => ForRarity(PotionRarity.Common);
    public static int Knife => ForRarity(RelicRarity.Uncommon);
    public static int Sigil => ForRarity(RelicRarity.Shop);

    public static async Task Apply(PlayerChoiceContext ctx, Creature enemy, int amount, Creature source)
    {
        if (!enemy.IsAlive) return;
        if (FmRival.SlotOf(enemy) is { } zone)
        {
            zone.AtkBonus -= amount; zone.DefBonus -= amount;
            FmField.NotifyChanged();
        }
        else await PowerCmd.Apply<FmShadowPower>(ctx, enemy, amount, source, null);
        FmSfx.Play(FmSfx.Trap);
        Log.Info($"[FMDuelist] Shadow Spell: {enemy.Name} loses {amount} ATK/DEF");
    }

    /// <summary>The relics' combat-start effects (called from the Millennium Puzzle once enemies are scaled).</summary>
    public static async Task CombatStart(Player owner, PlayerChoiceContext ctx)
    {
        var foes = owner.Creature.CombatState!.Enemies.Where(e => e.IsAlive && FmRival.Priest != e).ToList();
        if (foes.Count == 0) return;
        if (owner.GetRelic<UndyingSigil>() is { } sigil)
        {
            sigil.Flash();
            foreach (var e in foes) await Apply(ctx, e, Sigil, owner.Creature);
        }
        if (owner.GetRelic<BookRepairKnife>() is { } knife)
        {
            knife.Flash();
            await Apply(ctx, foes.OrderByDescending(e => e.CurrentHp).First(), Knife, owner.Creature);
        }
    }

    const string Tip = " ([gold]Shadow Spell[/gold]: its attacks deal that much less damage and its Block gains are that much smaller)";

    public static void ApplyLoc(LocManager loc)
    {
        try
        {
            loc.GetTable("relics").MergeWith(new Dictionary<string, string>
            {
                ["BOOK_REPAIR_KNIFE.description"] = $"At the start of each combat, the strongest enemy loses [blue]{Knife}[/blue] ATK and DEF." + Tip,
                ["UNDYING_SIGIL.description"] = $"At the start of each combat, ALL enemies lose [blue]{Sigil}[/blue] ATK and DEF." + Tip,
            });
            loc.GetTable("potions").MergeWith(new Dictionary<string, string>
            {
                ["POTION_OF_DOOM.description"] = $"An enemy loses [blue]{Potion}[/blue] ATK and DEF for this combat." + Tip,
            });
            loc.GetTable("powers").MergeWith(new Dictionary<string, string>
            {
                [ModelDb.GetEntry(typeof(FmShadowPower)) + ".title"] = "Shadow Spell",
                [ModelDb.GetEntry(typeof(FmShadowPower)) + ".description"] = "Lost ATK and DEF: its attacks deal this much less damage and its Block gains are this much smaller.",
                [ModelDb.GetEntry(typeof(FmShadowPower)) + ".smartDescription"] = "Lost ATK and DEF: its attacks deal this much less damage and its Block gains are this much smaller.",
            });
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] shadow loc: " + ex.Message); }
    }
}

/// <summary>Potion of Doom casts a Shadow Spell instead of applying Doom.</summary>
[HarmonyPatch(typeof(PotionOfDoom), "OnUse")]
static class PotionOfDoomPatch
{
    static bool Prefix(PotionOfDoom __instance, PlayerChoiceContext choiceContext, Creature? target, ref Task __result)
    {
        if (!FmRules.IsFmPlayer(__instance.Owner) || target == null) return true;
        __result = FmShadow.Apply(choiceContext, target, FmShadow.Potion, __instance.Owner.Creature);
        return false;
    }
}
