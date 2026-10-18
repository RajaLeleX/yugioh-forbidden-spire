using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using FMDuelist.Game;

namespace FMDuelist.Relics;

/// <summary>
/// Where a Forbidden Memories run finds relics: only in shops, from events (the festival among them) and from each
/// act's boss. Normal fights, elites and treasure chests give none (chests keep their Star Chips), and Heishin's
/// battle gives no relic either.
/// </summary>
public static class FmRelicDrops
{
    static readonly Random _rng = new();

    /// <summary>Fixes up a fight's rewards: relics only from a real act boss, and every act boss gives one.</summary>
    public static bool Apply(Player player, List<Reward> rewards, AbstractRoom? room)
    {
        if (room is not CombatRoom combat) return false;
        bool actBoss = combat.RoomType == RoomType.Boss && combat.Encounter is not Game.FmHeishinEncounter;
        int before = rewards.Count;
        rewards.RemoveAll(r => r is RelicReward && !actBoss);
        bool changed = rewards.Count != before;
        if (actBoss && !rewards.Any(r => r is RelicReward) && Random(player) is { } relic)
        {
            rewards.Add(new RelicReward(relic, player));
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// A Millennium Item you don't have yet: the common ones turn up most, the rare ones least (the festival's gift
    /// is never a rare one). Null once you have them all.
    /// </summary>
    public static RelicModel? Random(Player player, bool festival = false)
    {
        var left = FmMillenniumItems.All.Select(t => ModelDb.GetById<RelicModel>(ModelDb.GetId(t)))
            .Where(r => player.Relics.All(o => o.GetType() != r.GetType()))
            .Where(r => !festival || r.Rarity != RelicRarity.Rare).ToList();
        if (left.Count == 0) return null;
        double Weight(RelicModel r) => r.Rarity switch { RelicRarity.Common => 6, RelicRarity.Uncommon => 3, _ => 1 };
        double roll = _rng.NextDouble() * left.Sum(Weight);
        foreach (var r in left) { roll -= Weight(r); if (roll <= 0) return r.ToMutable(); }
        return left[^1].ToMutable();
    }
}

/// <summary>Treasure chests in a Forbidden Memories run hold Star Chips only, no relic.</summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
static class FmChestNoRelicPatch
{
    static bool Prefix(TreasureRoomRelicSynchronizer __instance)
    {
        if (RunManager.Instance.DebugOnlyGetState()?.Players.Any(FmRules.IsFmPlayer) != true) return true;
        __instance.CompleteWithNoRelics();
        return false;
    }
}
