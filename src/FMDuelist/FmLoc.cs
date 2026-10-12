using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Game;
using FMDuelist.Powers;
using FMDuelist.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist;

/// <summary>Card names and texts are built from the player's disc and merged into the game's loc tables.</summary>
public static class FmLoc
{
    static string E(Type t) => ModelDb.GetEntry(t);
    static string Clean(string s) => s.Replace("{", "(").Replace("}", ")");

    public static void Inject(LocManager loc)
    {
        try
        {
            var cards = new Dictionary<string, string>();
            foreach (var t in FmSheet.PoolTypes.Concat(FmSheet.ColorlessTypes).Append(typeof(FmEmptyZone)).Append(typeof(FmAttackPosition)).Append(typeof(FmDefensePosition)))
            {
                var m = (FmCardBase)Activator.CreateInstance(t)!;
                var (title, desc) = Describe(m);
                cards[E(t) + ".title"] = Clean(title);
                cards[E(t) + ".description"] = Clean(desc);
            }
            loc.GetTable("cards").MergeWith(cards);

            loc.GetTable("relics").MergeWith(new Dictionary<string, string>
            {
                [E(typeof(MillenniumPuzzle)) + ".title"] = "Millennium Puzzle",
                [E(typeof(MillenniumPuzzle)) + ".description"] = "Duel by Forbidden Memories rules: 8000 Life Points. Enemies have 100x HP and deal 100x damage. Draw up to 5 cards each turn and keep your hand. One summon per turn; right-click cards to fuse.",
                [E(typeof(MillenniumPuzzle)) + ".flavor"] = "Its pieces hold a memory older than the Spire.",
                [E(typeof(ExodiaCardBox)) + ".title"] = "Exodia Card Box",
                [E(typeof(ExodiaCardBox)) + ".description"] = "Add all five pieces of Exodia the Forbidden One to your deck (topped up to 15 cards if needed). Hold all five in your hand to obliterate every enemy and win the duel.\n[gold]15-card minimum:[/gold] for the rest of this run your deck can't go below [blue]15[/blue] cards (Cut at rest sites and card removal in shops stop at 15).",
                [E(typeof(ExodiaCardBox)) + ".flavor"] = "Sealed away, piece by piece.",
                [E(typeof(EquipBoosterPack)) + ".title"] = "Equip Booster Pack",
                [E(typeof(EquipBoosterPack)) + ".description"] = "Choose Megamorph or Bright Castle, then choose one of 3 other +500 ATK/DEF equip spells. Both go into your deck.",
                [E(typeof(EquipBoosterPack)) + ".flavor"] = "Fresh from the card shop.",
                [E(typeof(MagicProtectionKit)) + ".title"] = "Magic Protection Kit",
                [E(typeof(MagicProtectionKit)) + ".description"] = "Add Swords of Revealing Light and Spellbinding Circle to your deck.",
                [E(typeof(MagicProtectionKit)) + ".flavor"] = "A duelist never leaves home without a defense.",
                [E(typeof(DragonTreasure)) + ".title"] = "Dragon Treasure",
                [E(typeof(DragonTreasure)) + ".description"] = "Fusions may now create powerful Dragons (level 5+), like Twin-headed Thunder Dragon. Add Baby Dragon and Petit Dragon to your deck.",
                [E(typeof(DragonTreasure)) + ".flavor"] = "A hoard guarded by scales.",
            });

            var relicText = new Dictionary<string, string>();
            foreach (var (t, (title, text)) in FmRelicSheet.Text)
            {
                relicText[E(t) + ".title"] = title;
                relicText[E(t) + ".description"] = text;
                relicText[E(t) + ".flavor"] = "A memory from Forbidden Memories.";
            }
            loc.GetTable("relics").MergeWith(relicText);

            loc.GetTable("powers").MergeWith(new Dictionary<string, string>
            {
                [E(typeof(FmSwordsPower)) + ".title"] = "Swords of Revealing Light",
                [E(typeof(FmSwordsPower)) + ".description"] = "Every enemy is stunned on its turn. A boss duelist can still play cards but can't attack. Turns left shown.",
                [E(typeof(FmSwordsPower)) + ".smartDescription"] = "Every enemy is stunned on its turn. A boss duelist can still play cards but can't attack. Turns left shown.",
                [E(typeof(FmSpellboundPower)) + ".title"] = "Spellbinding Circle",
                [E(typeof(FmSpellboundPower)) + ".description"] = "Its attacks deal 1000 less damage. Turns left shown.",
                [E(typeof(FmSpellboundPower)) + ".smartDescription"] = "Its attacks deal 1000 less damage. Turns left shown.",
                [E(typeof(FmTrapPower)) + ".title"] = "Set Traps",
                [E(typeof(FmTrapPower)) + ".description"] = "Face-down trap cards. They spring when an enemy attacks.",
                [E(typeof(FmTrapPower)) + ".smartDescription"] = "Face-down trap cards. They spring when an enemy attacks.",
                [E(typeof(FmFieldPower)) + ".title"] = "Field",
                [E(typeof(FmFieldPower)) + ".description"] = "Monsters of the favored types get +500 ATK and DEF.",
                [E(typeof(FmFieldPower)) + ".smartDescription"] = "Monsters of the favored types get +500 ATK and DEF.",
            });

            var monsters = new Dictionary<string, string>();
            foreach (var (t, n) in new[] { (typeof(FMDuelist.Game.FmZonePet2), 2), (typeof(FMDuelist.Game.FmZonePet3), 3) })
            {
                monsters[E(t) + ".name"] = $"Monster Zone {n}";
                monsters[E(t) + ".moves.NOTHING_MOVE.title"] = "Stand Guard";
            }
            loc.GetTable("monsters").MergeWith(monsters);

            loc.GetTable("rest_site_ui").MergeWith(new Dictionary<string, string>
            {
                ["OPTION_FMCUT.name"] = "Cut",
                ["OPTION_FMCUT.description"] = "Remove cards from your deck: up to 1 in Act 1, 2 in Act 2, 3 in Act 3 (Exodia decks can't go below 15).",
            });

            loc.GetTable("card_selection").MergeWith(new Dictionary<string, string>
            {
                ["FMDUELIST_RITUAL"] = "Choose 3 monsters to offer to the ritual.",
            });
            Log.Info($"[FMDuelist] Localization injected ({cards.Count / 2} cards)");
        }
        catch (Exception ex) { Log.Error("[FMDuelist] Loc inject failed: " + ex); }
    }

    /// <summary>FM's own flavor text plus the monster types FM's equip table lets use it.</summary>
    static string EquipText(FmSpellCard s)
    {
        var info = FmData.Get(s.FmId);
        string works;
        if (s.FmId == 657) works = "any monster";
        else if (!FmData.EquipTargets.TryGetValue(s.FmId, out var ids) || ids.Count == 0) works = "no monsters";
        else
        {
            var byType = ids.Select(FmData.Get).Where(c => c.IsMonster).GroupBy(c => c.Type)
                .Select(g => (type: g.Key, n: g.Count(), total: FmData.Cards.Skip(1).Count(c => c != null && c.Type == g.Key)))
                .OrderByDescending(x => x.n).ToList();
            var main = byType.Where(x => x.n * 2 >= x.total || x.n >= 8).Select(x => FmData.TypeNames[(int)x.type]).ToList();
            var some = byType.Where(x => !(x.n * 2 >= x.total || x.n >= 8)).Select(x => FmData.TypeNames[(int)x.type]).ToList();
            works = byType.Count >= 15 ? "almost any monster"
                : (main.Count > 0 ? string.Join(", ", main) : "") + (some.Count > 0 ? (main.Count > 0 ? "; some " : "some ") + string.Join(", ", some) : "");
        }
        return $"Drag onto one of your monsters that can use it: +{s.Value} ATK/DEF while it stays on the field.\nWorks with: {works}.\n{info.Desc}";
    }

    static string Stats(int id)
    {
        var c = FmData.Get(id);
        return $"ATK {c.Atk} / DEF {c.Def}\nLv {c.Level} {FmData.TypeNames[(int)c.Type]}";
    }

    public static (string, string) Describe(FmCardBase m)
    {
        var info = FmData.Get(m.FmId);
        switch (m)
        {
            case FmEmptyZone: return ("Empty Zone", "Summon into this empty monster zone.");
            case FmAttackPosition: return ("Attack Position", "Upright. Click it and choose an enemy to attack for its ATK (once per turn). Attacked by a weaker hit: it isn't destroyed and the enemy takes the difference. An equal hit destroys it as it hits back for its ATK. Once it has attacked, it can't switch to Defense that turn.");
            case FmDefensePosition: return ("Defense Position", "On its side. At the end of each of your turns it gains Block equal to its DEF. Unbroken Block is dealt back to attackers.");
            case FmMonsterCard when FmExodia.Pieces.Contains(m.FmId): return (info.Name, Stats(m.FmId) + "\nHold all 5 pieces of the Forbidden One in your hand: obliterate every enemy.");
            case FmMonsterCard: return (info.Name, Stats(m.FmId) + "\nSummon (1 per turn). Right-click to fuse.");
            case FmRitualCard r: return (info.Name, $"Offer 3 monsters from your hand: summon {FmData.Get(r.ResultId).Name} ({FmData.Get(r.ResultId).Atk}/{FmData.Get(r.ResultId).Def}).");
            case FmTrapCard t: return (info.Name, "Set face-down. " + t.TrapKind switch
            {
                FmTrapKind.DestroyLe => $"When an enemy attack of {t.Value} or less hits, cancel it and deal {t.Value} damage to the attacker.",
                FmTrapKind.DestroyAny => "When an enemy attacks, cancel it and deal 3000 damage to the attacker.",
                FmTrapKind.ReflectBurn => "When an enemy attacks, reflect the damage back to it.",
                FmTrapKind.Weak => $"When an enemy attacks, cancel it and it loses {Game.FmStatRework.LossFor(t.Value)} ATK.",
                FmTrapKind.ReverseBuffs => "When an enemy attacks, cancel it and remove its buffs.",
                _ => "When an enemy attacks, cancel the hit.",
            });
            case FmSpellCard s: return (info.Name, s.Kind switch
            {
                FmSpellKind.Burn => $"Deal {s.Value} damage.",
                FmSpellKind.BurnSelfDmg => $"Deal {s.Value} damage. Take {s.Value / 2} damage.",
                FmSpellKind.BurnAll => $"Deal {s.Value} damage to ALL enemies." + (s.IsMassSpell ? " Only one Raigeki or Dark Hole can be played per turn." : ""),
                FmSpellKind.DarkHole => $"Deal {s.Value} damage to ALL enemies. All of your monsters are destroyed, and you can't summon any more monsters this turn. Can't be played after any of your monsters attacked this turn. Only one Raigeki or Dark Hole can be played per turn.",
                FmSpellKind.Heal => $"Recover {s.Value} Life Points.\nFuses with other healing spells into a stronger one (see the Fusing guide).",
                FmSpellKind.Invuln => $"Stun ALL enemies for {s.Value} turns. In a boss duel the boss can still play cards, but can't attack.",
                FmSpellKind.StopDefense => "Remove all Block from an enemy.",
                FmSpellKind.StripBuffs => "Remove all buffs from ALL enemies.",
                FmSpellKind.Stun when m.FmId == 349 => $"ALL enemies deal 1000 less damage for {s.Value} turns and are stunned for 1 turn (a boss duelist skips his turn).",
                FmSpellKind.Stun => "Stun an enemy: it can't act next turn.",
                FmSpellKind.Block => $"Gain {s.Value} Block.",
                FmSpellKind.BlockWeak => $"Gain {s.Value} Block. An enemy loses 500 ATK.",
                FmSpellKind.BlockCleanse => $"Gain {s.Value} Block. Remove your debuffs.",
                FmSpellKind.BlockHeal => $"Gain {s.Value} Block. Recover {s.Value} Life Points.",
                FmSpellKind.BurnStrongest => $"Deal {s.Value} damage to the enemy with the most HP.",
                FmSpellKind.WeakAll => $"ALL enemies lose {Game.FmStatRework.LossFor(s.Value)} ATK.",
                FmSpellKind.StripBlockAll => $"Remove all Block from ALL enemies, then deal {s.Value} damage to ALL enemies.",
                FmSpellKind.VulnerableAll => $"ALL enemies lose {Game.FmStatRework.LossFor(s.Value)} DEF.",
                FmSpellKind.Field => "Field: " + string.Join(", ", s.BoostTypes.Select(b => FmData.TypeNames[(int)b])) + " monsters get +500 ATK/DEF this combat.",
                FmSpellKind.Equip => EquipText(s),
                _ => info.Desc,
            });
        }
        return (info.Name, info.Desc);
    }
}
