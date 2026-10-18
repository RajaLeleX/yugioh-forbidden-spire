using FMDuelist.Cards;
using FMDuelist.Data;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using FMDuelist.Game;

namespace FMDuelist.Relics;

/// <summary>
/// The Millennium Items: the only relics a Forbidden Memories run finds (with the Millennium Puzzle that holds the
/// rules). None of them changes how a duel is played; each one changes what you win, the things a Forbidden
/// Memories speedrunner farms for: more Star Chips, better drops, the cards you're hunting. Rarer items do more.
/// </summary>
public abstract class FmMillenniumItem : RelicModel
{
    public abstract int ArtId { get; }
    public override bool IsAllowed(IRunState runState) => runState.Players.Any(FmRules.IsFmPlayer);

    static readonly Random _rng = new();
    protected static bool Roll(double chance) => _rng.NextDouble() < chance;
    protected static T Pick<T>(IReadOnlyList<T> list) => list[_rng.Next(list.Count)];

    /// <summary>The cards this act's rewards can give.</summary>
    protected static List<FmCardBase> Offered(Player p)
    {
        int act = p.RunState.CurrentActIndex;
        return ModelDb.CardPool<FmCardPool>().AllCards.OfType<FmCardBase>().Where(c => FmSheet.OfferedInAct(c, act)).ToList();
    }

    protected static CardModel Make(Player p, CardModel canonical) => p.RunState.CreateCard(canonical, p);

    /// <summary>Swaps one reward card (not one already matching) for a random card that matches.</summary>
    protected bool SwapOne(Player player, List<CardCreationResult> options, Func<FmCardBase, bool> wanted)
    {
        var matching = Offered(player).Where(wanted).Where(c => options.All(o => o.Card.GetType() != c.GetType())).ToList();
        var swappable = options.Where(o => o.Card is not FmCardBase fm || !wanted(fm)).ToList();
        if (matching.Count == 0 || swappable.Count == 0) return false;
        Pick(swappable).ModifyCard(Make(player, Pick(matching)), this);
        Flash();
        return true;
    }

    protected static FmCardInfo Info(FmCardBase c) => FmData.Get(c.FmId);
}

/// <summary>Common. More Star Chips from every duel and fight.</summary>
public sealed class FmItemKey : FmMillenniumItem
{
    public const decimal Bonus = 0.25m;
    public override int ArtId => 192;
    public override RelicRarity Rarity => RelicRarity.Common;

    public override decimal ModifyGoldGained(Player player, decimal amount)
    {
        if (player != Owner || amount <= 0 || player.RunState.CurrentRoom is not CombatRoom) return amount;
        return Math.Ceiling(amount * (1 + Bonus));
    }
}

/// <summary>Common. Card rewards lean towards Magic, Equip and Trap cards.</summary>
public sealed class FmItemScale : FmMillenniumItem
{
    public const double Chance = 0.4;
    public override int ArtId => 360;
    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
        => player == Owner && Roll(Chance) && SwapOne(player, options, c => Info(c).Type is FmType.Magic or FmType.Equip or FmType.Trap);
}

/// <summary>Uncommon. Card rewards lean towards Dragons (ones you can play).</summary>
public sealed class FmItemRod : FmMillenniumItem
{
    public const double Chance = 0.35;
    public override int ArtId => 220;
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        if (player != Owner || !Roll(Chance)) return false;
        bool strong = player.GetRelic<DragonTreasure>() != null;
        return SwapOne(player, options, c => Info(c) is { IsMonster: true, Type: FmType.Dragon } i && (strong || i.Level < FmRules.StrongDragonLevel));
    }
}

/// <summary>Uncommon. The plainest card in a reward may come up a rarity higher.</summary>
public sealed class FmItemRing : FmMillenniumItem
{
    public const double Chance = 0.35;
    public override int ArtId => 148;
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    static int Rank(CardRarity r) => r switch { CardRarity.Rare => 2, CardRarity.Uncommon => 1, _ => 0 };

    public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        if (player != Owner || options.Count == 0 || !Roll(Chance)) return false;
        int act = player.RunState.CurrentActIndex;
        int Of(CardModel c) => c is FmCardBase fm ? Rank(FmSheet.RarityFor(fm.FmId, act)) : 0;
        var lowest = options.OrderBy(o => Of(o.Card)).First();
        int want = Of(lowest.Card) + 1;
        if (want > 2) return false;
        var better = Offered(player).Where(c => Rank(FmSheet.RarityFor(c.FmId, act)) == want && options.All(o => o.Card.GetType() != c.GetType())).ToList();
        if (better.Count == 0) return false;
        lowest.ModifyCard(Make(player, Pick(better)), this);
        Flash();
        return true;
    }
}

/// <summary>Rare. Every card reward offers one more card to choose from.</summary>
public sealed class FmItemEye : FmMillenniumItem
{
    public override int ArtId => 109;
    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool TryModifyCardRewardOptionsLate(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        if (player != Owner || options.Count == 0) return false;
        var more = Offered(player).Where(c => options.All(o => o.Card.GetType() != c.GetType())).ToList();
        if (more.Count == 0) return false;
        options.Add(new CardCreationResult(Make(player, Pick(more))));
        return true;
    }
}

/// <summary>Rare. Elite duels, boss duels and the story's duelists give a second card reward.</summary>
public sealed class FmItemNecklace : FmMillenniumItem
{
    public override int ArtId => 260;
    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool TryModifyRewards(Player player, List<Reward> rewards, AbstractRoom? room)
    {
        if (player != Owner || room is not CombatRoom combat) return false;
        bool duel = combat.RoomType is RoomType.Elite or RoomType.Boss || Game.FmStoryAct1.Duels.Any(d => d.Encounter == combat.Encounter?.GetType());
        if (!duel || !rewards.Any(r => r is CardReward)) return false;
        rewards.Add(new CardReward(CardCreationOptions.ForRoom(player, combat.RoomType), 3, player));
        Flash();
        return true;
    }
}

public static class FmMillenniumItems
{
    public static readonly Type[] All = { typeof(FmItemKey), typeof(FmItemScale), typeof(FmItemRod), typeof(FmItemRing), typeof(FmItemEye), typeof(FmItemNecklace) };

    public static readonly Dictionary<Type, (string title, string text, string flavor)> Text = new()
    {
        [typeof(FmItemKey)] = ("Millennium Key", "Win [gold]25%[/gold] more Star Chips from duels and fights.", "It opens the door to the heart... and the treasury."),
        [typeof(FmItemScale)] = ("Millennium Scale", "Card rewards have a [blue]40%[/blue] chance to offer a Magic, Equip or Trap card.", "It weighs what you hold against what you need."),
        [typeof(FmItemRod)] = ("Millennium Rod", "Card rewards have a [blue]35%[/blue] chance to offer a Dragon.", "Its power bends the will of beasts."),
        [typeof(FmItemRing)] = ("Millennium Ring", "Card rewards have a [blue]35%[/blue] chance that their plainest card comes up a rarity higher.", "It points the way to what is rare."),
        [typeof(FmItemEye)] = ("Millennium Eye", "Card rewards offer [blue]1[/blue] more card to choose from.", "It sees every card in the pile."),
        [typeof(FmItemNecklace)] = ("Millennium Necklace", "Elite duels, boss duels and story duels give a second card reward.", "It foresees the spoils of victory."),
    };

    /// <summary>The relics a Forbidden Memories run may find: the Millennium Items, plus the mod's starter relics.</summary>
    /// (Each act's opening Ancient keeps its own starter relics, so Ancient relics stay allowed.)
    public static bool Allowed(RelicModel r) => r.Rarity == RelicRarity.Ancient
        || r is FmMillenniumItem or MillenniumPuzzle or ExodiaCardBox or MagicProtectionKit or DragonTreasure or EquipBoosterPack;
}
