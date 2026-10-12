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

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [
    ("using MegaCrit.Sts2.Core.Commands.Builders;",
     "using MegaCrit.Sts2.Core.Commands.Builders;\nusing MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;\nusing MegaCrit.Sts2.Core.Entities.RestSite;\nusing MegaCrit.Sts2.Core.Entities.Rewards;\nusing MegaCrit.Sts2.Core.Rewards;"),
    ("    // --- FM hand: draw up to 5, never discard the hand ---",
     """    // --- Skip really skips: the card reward is completed (gone) instead of waiting on the rewards screen ---
    public override bool TryModifyCardRewardAlternatives(Player player, CardReward cardReward, List<CardRewardAlternative> alternatives)
    {
        if (!Mine(player)) return false;
        int i = alternatives.FindIndex(a => a.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward);
        if (i < 0) return false;
        alternatives[i] = new CardRewardAlternative(alternatives[i].OptionId, PostAlternateCardRewardAction.EndSelectionAndCompleteReward);
        return true;
    }

    // --- Rest sites: Cut (remove up to 5 cards) replaces Smith; FM cards don't upgrade ---
    public override bool TryModifyRestSiteOptions(Player player, ICollection<RestSiteOption> options)
    {
        if (!Mine(player)) return false;
        var smith = options.FirstOrDefault(o => o is SmithRestSiteOption);
        if (smith != null) options.Remove(smith);
        options.Add(new FmCutRestSiteOption(player));
        return true;
    }

    // --- FM hand: draw up to 5, never discard the hand ---"""),
])

edit('src/FMDuelist/FmLoc.cs', [(
    '            loc.GetTable("card_selection").MergeWith(',
    '            loc.GetTable("rest_site_ui").MergeWith(new Dictionary<string, string>\n            {\n'
    '                ["OPTION_FMCUT.name"] = "Cut",\n'
    '                ["OPTION_FMCUT.description"] = "Remove up to 5 cards from your deck.",\n            });\n\n'
    '            loc.GetTable("card_selection").MergeWith(')])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in [{"id": "skip_reward", "what": "Card reward Skip completes (discards) the reward", "value": 0, "hook": "card_pool"},
          {"id": "rest_cut", "what": "Rest site Cut replaces Smith: remove up to 5 cards from the deck", "value": 5, "hook": "card_pool"}]:
    if not any(x['id'] == r['id'] for x in rows): rows.append(r)
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
