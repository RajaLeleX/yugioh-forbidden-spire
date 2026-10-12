using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using FMDuelist.Cards;
using FMDuelist.Game;

namespace FMDuelist.Relics;

/// <summary>
/// Act 2's opening Ancient offers a bonus "Equip Booster Pack": pick Megamorph or Bright Castle (FM's "white castle"),
/// then pick one of three other +500 ATK/DEF equip spells. Both go into your deck.
/// </summary>
public sealed class EquipBoosterPack : RelicModel
{
    public const int Megamorph = 657, BrightCastle = 668;
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override bool IsAllowedInShops => false;

    public override async Task AfterObtained()
    {
        var ctx = new BlockingPlayerChoiceContext();
        try
        {
            await Pick(ctx, new[] { Megamorph, BrightCastle });
            var others = FmSheet.PoolTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t))).OfType<FmSpellCard>()
                .Where(s => s.Kind == FmSpellKind.Equip && s.Value == 500 && s.FmId != BrightCastle && s.FmId != Megamorph)
                .Select(s => s.FmId).Distinct().ToList();
            var rng = Owner.PlayerRng.Rewards;
            var three = new List<int>();
            while (three.Count < 3 && others.Count > 0) { int i = rng.NextInt(0, others.Count); three.Add(others[i]); others.RemoveAt(i); }
            await Pick(ctx, three);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] equip booster pack: " + ex.Message); }
    }

    async Task Pick(BlockingPlayerChoiceContext ctx, IEnumerable<int> ids)
    {
        var options = ids.Select(id => Owner.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), Owner)).ToList();
        if (options.Count == 0) return;
        var pick = await CardSelectCmd.FromChooseACardScreen(ctx, options, Owner, canSkip: false) ?? options[0];
        await CardPileCmd.Add(pick, PileType.Deck);
    }
}

/// <summary>The Act 2 Ancient (whichever one appears) also offers the Equip Booster Pack.</summary>
[HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
static class EquipBoosterOfferPatch
{
    static readonly MethodInfo RelicOption = AccessTools.Method(typeof(AncientEventModel), "RelicOption", new[] { typeof(RelicModel), typeof(string), typeof(string) });

    static void Postfix(AncientEventModel __instance, ref IReadOnlyList<EventOption> __result)
    {
        try
        {
            if (__instance is Neow || !FmRules.IsFmPlayer(__instance.Owner) || __instance.Owner!.RunState.CurrentActIndex != 1) return;
            if (__result.Count <= 1 || __result.Any(o => o.TextKey.Contains("EQUIP_BOOSTER"))) return;
            var opt = (EventOption)RelicOption.Invoke(__instance, new object?[] { ModelDb.Relic<EquipBoosterPack>().ToMutable(), "INITIAL", null })!;
            if (__result is List<EventOption> list) list.Add(opt);
            else __result = __result.Append(opt).ToList();
            Log.Info($"[FMDuelist] Act 2 Ancient {__instance.Id.Entry}: Equip Booster Pack offered ({__result.Count} choices)");
        }
        catch (Exception ex) { Log.Error("[FMDuelist] equip booster offer: " + ex); }
    }
}
