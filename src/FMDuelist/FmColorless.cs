using FMDuelist.Cards;
using FMDuelist.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace FMDuelist;

/// <summary>While an FM duelist is in the run, the colorless pool is FM's neutral cards (rituals and support magic).</summary>
[HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.AllCards), MethodType.Getter)]
static class ColorlessPoolPatch
{
    static CardModel[]? _fm;

    static void Postfix(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        if (__instance is not ColorlessCardPool || !FmRules.RunActive) return;
        _fm ??= FmSheet.ColorlessTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t))).ToArray();
        __result = _fm;
    }
}
