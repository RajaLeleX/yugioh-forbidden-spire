using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace FMDuelist;

/// <summary>
/// In FM runs, base-game flat heals and max-HP changes (relics, events, potions, Neow, enemy regeneration) are x100
/// so they match 8000 LP and x100 enemies. FM's own effects run inside FmRules.Raw and keep their FM numbers.
/// </summary>
static class FmBalance
{
    public static bool Scales(Creature c, decimal amount) =>
        FmRules.RunActive && FmRules.RawDepth == 0 && amount > 0 && amount < FmRules.FlatLimit && c.Monster is not Osty;
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
static class HealScalePatch
{
    static void Prefix(Creature creature, ref decimal amount)
    {
        if (FmBalance.Scales(creature, amount)) amount *= FmRules.EnemyScale;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp))]
static class GainMaxHpScalePatch
{
    static void Prefix(Creature creature, ref decimal amount)
    {
        if (FmBalance.Scales(creature, amount)) amount *= FmRules.EnemyScale;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseMaxHp))]
static class LoseMaxHpScalePatch
{
    static void Prefix(Creature creature, ref decimal amount)
    {
        if (FmBalance.Scales(creature, amount)) amount *= FmRules.EnemyScale;
    }
}
