using MegaCrit.Sts2.Core.Nodes.CommonUi;
using System.Text.RegularExpressions;
using FMDuelist.Cards;
using FMDuelist.Data;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Game;

/// <summary>
/// No upgrades in Forbidden Memories, so in Duel Master K runs every "Upgrade" becomes "Transform +": the card turns
/// into a random card exactly one rarity higher (starter/basic -> Common -> Uncommon -> Rare; a Rare becomes another
/// Rare). Monsters stay monsters; spells, traps and equips stay spells, traps and equips. All the game's text that
/// mentions upgrading says Transform + instead.
/// </summary>
public static class FmTransformPlus
{
    static int Tier(CardRarity r) => r switch { CardRarity.Common => 0, CardRarity.Uncommon => 1, CardRarity.Rare => 2, _ => -1 };

    public static CardModel? Pick(CardModel original)
    {
        if (original is not FmCardBase fm || original.Owner is not { } owner || !FmRules.IsFmPlayer(owner)) return null;
        int act = FmRules.ActIndex;
        bool monster = original is FmMonsterCard;
        int target = Math.Min(2, Tier(original.Rarity) + 1);
        var pool = FmSheet.PoolTypes
            .Select(t => int.TryParse(t.Name.Replace("FmCard", ""), out int id) ? id : 0)
            .Where(id => id > 0 && id != fm.FmId && id is not (>= 17 and <= 21) && FmData.Get(id).IsMonster == monster)
            .Where(id => !monster || FmFusion.DragonAllowed(id, owner))
            .Where(id => Tier(FmSheet.RarityFor(id, act)) == target).ToList();
        if (pool.Count == 0) return null;
        var rng = owner.PlayerRng.Rewards;
        int pick = pool[rng.NextInt(0, pool.Count)];
        Log.Info($"[FMDuelist] Transform + {fm.Info.Name} ({original.Rarity}) -> {FmData.Get(pick).Name} ({FmSheet.RarityFor(pick, act)})");
        return original.CardScope!.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(pick))), owner);
    }

    // ---------------- text ----------------
    static readonly (Regex re, string to)[] Words =
    {
        (new(@"\[gold\]Upgraded\[/gold\]"), "[gold]Transformed +[/gold]"),
        (new(@"\[gold\]Upgrade\[/gold\]"), "[gold]Transform +[/gold]"),
        (new(@"\[gold\]Upgraded"), "[gold]Transformed +"),
        (new(@"\bUpgrades?\b"), "Transform +"),
        (new(@"\bupgrades?\b"), "Transform +"),
        (new(@"\bUpgraded\b"), "Transformed +"),
        (new(@"\bupgraded\b"), "Transformed +"),
    };

    /// <summary>Rewrites the words outside of {placeholders}.</summary>
    static string Rewrite(string v)
    {
        var parts = Regex.Split(v, @"(\{[^}]*\})");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("{")) continue;
            foreach (var (re, to) in Words) parts[i] = re.Replace(parts[i], to);
        }
        return string.Concat(parts);
    }

    static readonly string[] Tables = { "relics", "potions", "events", "cards", "powers", "ancients", "enchantments", "card_selection", "gameplay_ui", "map", "rest_site_ui", "static_hover_tips" };
    static readonly HashSet<string> SkipKeys = new() { "VIEW_UPGRADES", "SHOW_UPGRADE" };

    public static void ApplyLoc(LocManager loc)
    {
        int n = 0;
        foreach (var name in Tables)
        {
            try
            {
                var t = loc.GetTable(name);
                var tr = AccessTools.FieldRefAccess<LocTable, Dictionary<string, string>>("_translations")(t);
                var upd = new Dictionary<string, string>();
                foreach (var (k, v) in tr)
                    if (!SkipKeys.Contains(k) && v.Contains("pgrad") && Rewrite(v) is var r && r != v) upd[k] = r;
                t.MergeWith(upd);
                n += upd.Count;
            }
            catch (Exception ex) { Log.Warn($"[FMDuelist] Transform + text ({name}): {ex.Message}"); }
        }
        Log.Info($"[FMDuelist] Transform +: {n} texts reworded");
    }
}

[HarmonyPatch]
static class FmTransformPlusPatch
{
    /// <summary>FM cards can be picked wherever the game lets you pick a card to upgrade.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CardModel), nameof(CardModel.IsUpgradable), MethodType.Getter)]
    static void Upgradable(CardModel __instance, ref bool __result)
    {
        if (!__result && __instance is FmCardBase && __instance.Owner is { } o && FmRules.IsFmPlayer(o)) __result = true;
    }

    /// <summary>Upgrading an FM card transforms it instead (Transform +).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Upgrade), new[] { typeof(IEnumerable<CardModel>), typeof(CardPreviewStyle) })]
    static void Upgrade(ref IEnumerable<CardModel> cards, CardPreviewStyle style)
    {
        var list = cards.ToList();
        var fm = list.Where(c => c is FmCardBase && c.Owner is { } o && FmRules.IsFmPlayer(o)).ToList();
        if (fm.Count == 0) return;
        cards = list.Except(fm).ToList();
        foreach (var card in fm)
            if (FmTransformPlus.Pick(card) is { } replacement)
                TaskHelper.RunSafely(CardCmd.Transform(card, replacement, style));
    }
}
