import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))
p = 'Relics/MillenniumPuzzle.cs'
s = open(p, encoding='utf-8').read()
a = '''    // --- Act 1: each card reward has a 20% chance to offer a weak Dragon ---
    public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        if (!Mine(player) || player.RunState.CurrentActIndex != 0 || options.Count == 0 || creationOptions.CustomCardPool != null) return false;'''
b = '''    // --- Swords of Revealing Light / Spellbinding Circle: never in Act 1, very uncommon in Act 2, uncommon in Act 3 ---
    static readonly int[] KitCards = { 348, 349 };
    const float KitAct2KeepChance = 0.3f;

    static bool IsKitCard(CardModel? c) => c is FmCardBase f && KitCards.Contains(f.FmId);

    /// <summary>Whether a Swords/Spellbinding that the game rolled may stay (Act 3 keeps its normal Uncommon odds).</summary>
    static bool KitMayAppear(Player player) => player.RunState.CurrentActIndex switch
    {
        0 => false,
        1 => player.PlayerRng.Rewards.NextFloat() < KitAct2KeepChance,
        _ => true,
    };

    /// <summary>A different Uncommon FM card in its place.</summary>
    static CardModel KitReplacement(Player player)
    {
        var pool = FmSheet.PoolTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t)))
            .Where(c => c.Rarity == CardRarity.Uncommon && !IsKitCard(c)).ToList();
        var pick = pool[player.PlayerRng.Rewards.NextInt(0, pool.Count)];
        return player.RunState.CreateCard(pick, player);
    }

    static bool GateKit(Player player, IEnumerable<CardCreationResult> options)
    {
        bool changed = false;
        foreach (var o in options)
            if (IsKitCard(o.Card) && !KitMayAppear(player)) { o.ModifyCard(KitReplacement(player)); changed = true; }
        return changed;
    }

    // --- Act 1: each card reward has a 20% chance to offer a weak Dragon ---
    public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        bool gated = Mine(player) && GateKit(player, options);
        return TryAct1Dragon(player, options, creationOptions) || gated;
    }

    private bool TryAct1Dragon(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        if (!Mine(player) || player.RunState.CurrentActIndex != 0 || options.Count == 0 || creationOptions.CustomCardPool != null) return false;'''
assert a in s; s = s.replace(a, b, 1)
a = '''    public override void ModifyMerchantCardCreationResults(Player player, List<CardCreationResult> cards)
    {
        if (!Mine(player) || !_shopDragonPending) return;'''
b = '''    public override void ModifyMerchantCardCreationResults(Player player, List<CardCreationResult> cards)
    {
        if (Mine(player)) GateKit(player, cards);
        if (!Mine(player) || !_shopDragonPending) return;'''
assert a in s; s = s.replace(a, b, 1)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
