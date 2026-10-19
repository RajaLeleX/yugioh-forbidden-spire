using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace FMDuelist.Game;

/// <summary>
/// Shops are Forbidden Memories' card shop: the shop owner behind his counter (a picture from the player's own
/// disc, local only), "Hello there!", and FM's menu with BUY (the game's own shop), SELL (one card per visit for
/// half its shop price, rounded up), SAVE AND QUIT (as from the pause menu; reloading comes back to the shop) and
/// LEAVE SHOP (back to the map, the shop done).
/// </summary>
public static class FmCardShop
{
    const string Picture = "bg_cardshop";
    static readonly string[] Options = { "BUY", "SELL", "DECK", "SAVE AND QUIT", "LEAVE SHOP" };

    /// <summary>What a card fetches: half of what the shop asks for it, rounded up (no fractions of a Star Chip).</summary>
    public static int SellPrice(CardModel card)
    {
        int price = card.Rarity switch { CardRarity.Rare => 150, CardRarity.Uncommon => 75, _ => 50 };
        if (card.Pool is ColorlessCardPool) price = Mathf.RoundToInt(price * 1.15f);
        return (price + 1) / 2;
    }

    /// <summary>The room behind the menu: the shop picture instead of the game's shop, its people and its proceed button.</summary>
    internal static void Dress(NMerchantRoom room)
    {
        if (FmScene.Tex(Picture) is not { } tex) return;
        Node? keep = room.Inventory;
        while (keep != null && keep.GetParent() != room) keep = keep.GetParent();
        foreach (var c in room.GetChildren().OfType<CanvasItem>())
            if (c != keep) c.Visible = false;
        room.MerchantButton.Visible = false;
        room.ProceedButton.Visible = false;
        if (Traverse.Create(room).Field("_characterContainer").GetValue() is CanvasItem people) people.Visible = false;
        var pic = new TextureRect
        {
            Name = "FmCardShop", Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        pic.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        room.AddChild(pic);
        room.MoveChild(pic, 0);
    }

    internal static async Task Visit(NMerchantRoom room, Player player)
    {
        FmScene? sc = null;
        try
        {
            sc = await FmScene.Open();
            if (sc == null) return;
            sc.HideSkip();
            await sc.Bg(Picture);
            await sc.Lit("shop", "Hello there!");
            bool sold = false;
            while (GodotObject.IsInstanceValid(room))
            {
                switch (await sc.Menu(Options))
                {
                    case 0: // BUY: the game's own shop window
                        await FmFade.Out();
                        sc.SetVisible(false);
                        room.OpenInventory();
                        await FmFade.In();
                        while (GodotObject.IsInstanceValid(room) && room.Inventory.IsOpen) await sc.Wait(0.1);
                        await FmFade.Out();
                        sc.SetVisible(true);
                        await FmFade.In();
                        break;
                    case 1: // SELL one card
                        if (sold) { await sc.Lit("shop", "I already bought a card from you today. Come back another time!"); break; }
                        if (player.Deck.Cards.Count <= FmRules.MinDeckFor(player)) { await sc.Lit("shop", "Your deck is too small to sell any cards!"); break; }
                        sc.SetVisible(false);
                        var prefs = new CardSelectorPrefs(new LocString("card_selection", "FMDUELIST_SELL"), 1) { Cancelable = true };
                        var card = (await CardSelectCmd.FromDeckForRemoval(player, prefs)).FirstOrDefault();
                        sc.SetVisible(true);
                        if (card == null) break;
                        int price = SellPrice(card);
                        await sc.Lit("shop", $"I'll pay {price} Star Chips for {card.Title}.");
                        if (await sc.Choose("Sell it", "Keep it") != 0) { await sc.Lit("shop", "Suit yourself."); break; }
                        await CardPileCmd.RemoveFromDeck(card);
                        await PlayerCmd.GainGold(price, player);
                        sold = true;
                        await sc.Lit("shop", "Thank you! Come again!");
                        break;
                    case 2: // Build Deck, then back to the menu
                        sc.SetVisible(false);
                        await FmDeckEditor.Open(player);
                        sc.SetVisible(true);
                        break;
                    case 3: // SAVE AND QUIT, like the pause menu (the run reloads into this shop)
                        await sc.Close(); sc = null;
                        NRunMusicController.Instance?.StopMusic();
                        await NGame.Instance!.ReturnToMainMenu();
                        return;
                    default: // LEAVE SHOP
                        await sc.Close(); sc = null;
                        if (GodotObject.IsInstanceValid(room)) room.ProceedButton.Visible = true; // if the map is closed again
                        NMapScreen.Instance.Open();
                        return;
                }
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] card shop: " + ex); }
        finally
        {
            if (sc != null) await sc.Close();
        }
    }
}

[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom._Ready))]
static class FmCardShopPatch
{
    static void Postfix(NMerchantRoom __instance)
    {
        try
        {
            var player = RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault(FmRules.IsFmPlayer);
            if (player == null || FmScene.Tex("bg_cardshop") == null) return;
            FmCardShop.Dress(__instance);
            TaskHelper.RunSafely(FmCardShop.Visit(__instance, player));
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] card shop setup: " + ex.Message); }
    }
}
