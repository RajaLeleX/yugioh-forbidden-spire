import os, json
H = os.path.expanduser('~/fmsts')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

edit('src/FMDuelist/Game/FmRules.cs', [(
    "    public const int StrongDragonLevel = 5;",
    "    public const int StrongDragonLevel = 5;\n    /// <summary>Decks can't be cut below this (keeps Exodia runs honest).</summary>\n    public const int MinDeck = 15;")])

edit('src/FMDuelist/Game/FmRestSite.cs', [
    ("    public override bool IsEnabled => Owner.Deck.Cards.Count > 1;",
     "    public override bool IsEnabled => Owner.Deck.Cards.Count > FmRules.MinDeck;"),
    ("        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1, Math.Min(MaxCuts, Owner.Deck.Cards.Count - 1))",
     "        int allowed = Math.Min(MaxCuts, Owner.Deck.Cards.Count - FmRules.MinDeck);\n        if (allowed <= 0) return false;\n"
     "        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1, allowed)"),
])

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    ("    // Exodia pieces are never sold",
     "    // the shop's card removal also respects the minimum deck size\n"
     "    public override bool ShouldAllowMerchantCardRemoval(Player player) => !Mine(player) || player.Deck.Cards.Count > FmRules.MinDeck;\n\n"
     "    // Exodia pieces are never sold"),
    # Exodia box tops the deck up to the minimum with random reward-pool cards
    ("            await CardPileCmd.Add(card, PileType.Deck);\n        }\n    }\n}\n\n/// <summary>Run-start blessing",
     "            await CardPileCmd.Add(card, PileType.Deck);\n        }\n"
     "        var pool = ModelDb.CardPool<FmCardPool>().AllCards.Where(c => FmSheet.OfferedInAct(c, 0)).ToList();\n"
     "        var rng = new Random();\n"
     "        while (Owner.Deck.Cards.Count < FmRules.MinDeck && pool.Count > 0)\n"
     "            await CardPileCmd.Add(Owner.RunState.CreateCard(pool[rng.Next(pool.Count)], Owner), PileType.Deck);\n"
     "    }\n}\n\n/// <summary>Run-start blessing"),
])
edit('src/FMDuelist/FmLoc.cs', [
    ('["OPTION_FMCUT.description"] = "Remove up to 5 cards from your deck.",',
     '["OPTION_FMCUT.description"] = "Remove up to 5 cards from your deck (your deck can\'t go below 15).",'),
    ('"Add all five pieces of Exodia the Forbidden One to your deck. Hold all five in your hand to obliterate every enemy and win the duel."',
     '"Add all five pieces of Exodia the Forbidden One to your deck (topped up to 15 cards if needed). Hold all five in your hand to obliterate every enemy and win the duel. Decks can\'t be cut below 15."'),
])
grep_check = open(f'{H}/src/FMDuelist/Relics/MillenniumPuzzle.cs').read()
sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'min_deck' for r in rows):
    rows.append({"id": "min_deck", "what": "Deck can't be cut (rest Cut, shop removal) below 15; Exodia box tops the deck up to 15 with random reward-pool cards", "value": 15, "hook": "card_pool"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
