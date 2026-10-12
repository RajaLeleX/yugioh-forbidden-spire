using FMDuelist.Data;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;

namespace FMDuelist.Game;

/// <summary>Values from sheets/systems.json.</summary>
public static class FmRules
{
    public const int LifePoints = 8000;
    public const int EnemyScale = 100;
    public const int HandSize = 5;
    public const int MonsterHp = 1;
    public const int StrongDragonLevel = 5;
    /// <summary>Decks can't be cut below this (keeps Exodia runs honest).</summary>
    public const int MinDeck = 15;
    /// <summary>The minimum only applies to Exodia runs; other decks can be cut down to 1 card.</summary>
    public static int MinDeckFor(MegaCrit.Sts2.Core.Entities.Players.Player p) => p.GetRelic<Relics.ExodiaCardBox>() != null ? MinDeck : 1;

    /// <summary>0-based act index of the current run, kept up to date by the Millennium Puzzle relic.</summary>
    public static int ActIndex;

    /// <summary>True while the current run has an FM duelist (set from the run hook subscription).</summary>
    public static bool RunActive;

    /// <summary>While > 0, damage/block being dealt is already in FM numbers (not multiplied by 100).</summary>
    public static int RawDepth;

    /// <summary>Base-game flat numbers (HP loss, heals, max HP) are small; anything this size or bigger was
    /// already computed from FM-scale values (e.g. 30% of 8000 LP) and must not be scaled again.</summary>
    public const decimal FlatLimit = 200m;
    public static async Task Raw(Func<Task> f) { RawDepth++; try { await f(); } finally { RawDepth--; } }

    /// <summary>What block-multiplying items give in FM runs: this much DEF until your next turn.</summary>
    public const int TempDefBoost = 1000;

    public static bool IsFmPlayer(Player? p) => p != null && p.Character is Necrobinder && p.GetRelic<Relics.MillenniumPuzzle>() != null;

    public static CardRarity ParseRarity(string s) => s switch
    {
        "Common" => CardRarity.Common,
        "Uncommon" => CardRarity.Uncommon,
        "Rare" => CardRarity.Rare,
        _ => CardRarity.None,
    };
}
