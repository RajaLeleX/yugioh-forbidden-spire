using FMDuelist.Cards;
using FMDuelist.Data;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using System.Reflection;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace FMDuelist.Game;

/// <summary>
/// Boss duels: the Act 1 boss is the Kin Priest, who duels you by Forbidden Memories rules with a deck drawn from
/// Priest Seto's first-duel card pool on your disc. He has 8000 Life Points, 3 monster zones, a 5-card hand, one summon
/// per turn, and always makes the best fusion he can. Battles on both sides use FM rules:
///   Attack vs Attack: the stronger destroys the weaker and its owner loses the difference (a tie destroys both);
///   Attack vs Defense: ATK above DEF destroys it with no damage; ATK below DEF costs the attacker's owner the difference.
/// Nobody can be attacked directly while they still have a monster. Raigeki and Dark Hole only destroy monsters.
/// </summary>
public static class FmRival
{
    /// <summary>A duelist holds a hand of 5 cards and plays only from those, as in FM...</summary>
    public const int HandSize = 5;
    /// <summary>...except the final bosses at the end of Act 3, who secretly choose from a hidden pool of 20.</summary>
    public const int HiddenHandSize = 20;

    /// <summary>The duelists who play from the hidden 20-card pool (the six final bosses of Act 3, once they're in).</summary>
    public static readonly HashSet<int> FinalBosses = new();

    /// <summary>How many cards he holds to choose from.</summary>
    static int PoolSize => Current is { } c && FinalBosses.Contains(c.DuelistIndex) ? HiddenHandSize : HandSize;
    /// <summary>Face-down cards still shown this turn (5 at the start of his turn, fewer as he plays them).</summary>
    static int _visible;

    /// <summary>A boss duelist: which StS boss it replaces, its name, and which FM duelist's deck it draws from.</summary>
    public sealed record Duelist(Type Encounter, Type Monster, string Name, int DuelistIndex, int LifePoints, int Act);

    public static readonly Duelist[] Bosses =
    {
        new(typeof(TheKinBoss), typeof(KinPriest), "Young Priest Seto", 7, 8000, 0),          // Seto, his first duel
        new(typeof(KnowledgeDemonBoss), typeof(KnowledgeDemon), "\"Final\" Duelist", 17, 8000, 1), // Seto Kaiba, World Tournament finals
        new(typeof(QueenBoss), typeof(Queen), "Final Priest Seto", 36, 8000, 2),             // Seto 3rd, before DarkNite
    };

    /// <summary>The bosses plus the main menu's practice opponent.</summary>
    public static IEnumerable<Duelist> AllDuelists => Bosses.Append(FmPractice.Duelist).Append(FmAct1Story.Heishin).Concat(FmStoryAct1.Duelists);
    public static bool IsDuelist(Creature? c) => c?.Monster != null && AllDuelists.Any(b => b.Monster == c.Monster.GetType());

    public static Duelist? Current;
    public static int LifePoints => Current?.LifePoints ?? 8000;

    public static bool Active;
    public static Creature? Priest;
    /// <summary>Spellbinding Circle: he skips his next turn.</summary>
    public static bool Bound;
    /// <summary>Spellbinding Circle: his monsters lose 500 ATK and DEF while it lasts.</summary>
    public static int BoundAtk => Priest?.GetPower<Powers.FmSpellboundPower>() != null ? 500 : 0;
    public static readonly FmSlot[] Slots = { new(0, true), new(1, true), new(2, true), new(3, true), new(4, true) };

    /// <summary>How many traps he has set face down.</summary>
    public static int TrapCount => Traps.Count;

    /// <summary>Cards left in his deck.</summary>
    public static int DeckCount => Deck.Count;
    static readonly List<int> Deck = new();
    static readonly List<int> Hand = new();
    static readonly Dictionary<int, int[]> _weights = new();
    static readonly System.Random Rng = new();

    /// <summary>
    /// How well a duelist plays, after FM's own difficulty ladder: the early story duelists mostly play their
    /// strongest single card and only now and then fuse even when they could; later duelists fuse (up to three
    /// cards, and onto their field), use their spells, equips and traps well and rarely misplay.
    /// </summary>
    /// <param name="Fuse">Chance each turn that he looks for a fusion at all.</param>
    /// <param name="Chain">Most cards in one fusion (2 or 3).</param>
    /// <param name="Onto">Whether he fuses onto a monster already on his field.</param>
    /// <param name="Best">Chance he plays his strongest monster (otherwise a random one from his hand).</param>
    /// <param name="Equip">Chance per turn that he plays his equips.</param>
    /// <param name="Trap">Chance per turn that he sets a trap.</param>
    /// <param name="Spell">Chance per turn that he uses Raigeki / Dark Hole / burns / heals when they'd help.</param>
    public sealed record Skill(double Fuse, int Chain, bool Onto, double Best, double Equip, double Trap, double Spell);

    static readonly Skill Full = new(1, 3, true, 1, 1, 1, 1);

    /// <summary>By FM duelist number.</summary>
    static readonly Dictionary<int, Skill> Skills = new()
    {
        [1] = new(0.15, 2, false, 0.55, 0.30, 0.30, 0.35), // Simon Muran: single cards, the odd fusion
        [2] = new(0.20, 2, false, 0.60, 0.35, 0.35, 0.40), // Teana
        [4] = new(0.25, 2, false, 0.60, 0.35, 0.35, 0.40), // Villager 1
        [5] = new(0.30, 2, false, 0.65, 0.40, 0.40, 0.45), // Villager 2
        [3] = new(0.30, 2, false, 0.65, 0.40, 0.40, 0.45), // Jono
        [6] = new(0.35, 2, true, 0.70, 0.45, 0.45, 0.50),  // Villager 3
        [7] = new(0.75, 3, true, 0.90, 0.80, 0.80, 0.85),  // Young Priest Seto
    };

    public static Skill Me => Skills.GetValueOrDefault(Current?.DuelistIndex ?? 0, Full);
    static bool Roll(double chance) => chance >= 1 || Rng.NextDouble() < chance;

    public static bool IsZoneCreature(Creature? c) => c != null && Slots.Any(s => s.RivalCreature == c);
    public static FmSlot? SlotOf(Creature? c) => c == null ? null : Slots.FirstOrDefault(s => s.RivalCreature == c);
    public static IEnumerable<FmSlot> Living => Slots.Where(s => s.MonsterId != 0 && s.RivalCreature is { IsAlive: true });
    public static bool HasMonster => Living.Any();

    /// <summary>What your monsters may attack: his monsters, or him once his field is empty.</summary>
    public static bool CanBeAttacked(Creature c) =>
        SlotOf(c) is { } s ? s.MonsterId != 0 : c == Priest && !HasMonster;

    // ---------------- setup ----------------
    public static async Task Begin(Player player, ICombatState combat)
    {
        Reset();
        foreach (var b in AllDuelists)
            if (combat.Enemies.FirstOrDefault(e => e.IsAlive && e.Monster != null && e.Monster.GetType() == b.Monster) is { } boss) { Priest = boss; Current = b; break; }
        if (Priest == null || Current == null || !FmData.Loaded) return;
        Active = true;
        FmDuelStats.Reset();
        if (Current == FmPractice.Duelist && NCombatRoom.Instance?.GetCreatureNode(Priest) is { } mirror)
            mirror.Visuals.Scale = new Vector2(-Math.Abs(mirror.Visuals.Scale.X), mirror.Visuals.Scale.Y); // the mirror Duel Master K faces you
        // the boss's own buffs and passives don't belong in a duel
        foreach (var pw in Priest.Powers.ToList()) await PowerCmd.Remove(pw);
        await FmRules.Raw(() => CreatureCmd.SetMaxAndCurrentHp(Priest, LifePoints));
        // five monster zones for him (invisible stand-ins under his cards)
        MonsterModel[] zones = { ModelDb.Monster<FmRivalZone1>(), ModelDb.Monster<FmRivalZone2>(), ModelDb.Monster<FmRivalZone3>(), ModelDb.Monster<FmRivalZone4>(), ModelDb.Monster<FmRivalZone5>() };
        if (Current == FmPractice.Duelist) FmPractice.Layout(player, Priest);
        for (int i = 0; i < zones.Length; i++)
        {
            var c = await CreatureCmd.Add(zones[i].ToMutable(), combat, CombatSide.Enemy, null);
            Slots[i].RivalCreature = c;
        }
        foreach (var s in Slots) s.Visual.Attach(player);
        FmDuelLayout.Duel(player);
        if (Priest.Monster is { } pm) pm.SetMoveImmediate(Idle(), true);
        BuildDeck();
        Draw();
        _visible = Math.Min(HandSize, Hand.Count);
        FmRivalHand.Show(_visible);
        Log.Info($"[FMDuelist] Duel with {Current.Name} (FM duelist #{Current.DuelistIndex}): {LifePoints} LP, deck {Deck.Count}, hand {string.Join(", ", Hand.Select(h => FmData.Get(h).Name))}");
    }

    public static void Reset()
    {
        Active = false; Bound = false; Priest = null; Current = null; Deck.Clear(); Hand.Clear(); Traps.Clear();
        _emptyChecks = 0; _emptyLimit = 2 + Rng.Next(2);
        FmDeckBuild.DeckedOut = false;
        try { FmFieldHud.Clear(); FmLifePoints.Clear(); } catch { }
        FmRivalHand.Hide();
        FmPractice.RemoveLabels();
        foreach (var s in Slots) { s.Clear(); s.RivalCreature = null; }
    }

    static MoveState Idle()
    {
        var idle = new MoveState("FM_DUEL_MOVE", (IReadOnlyList<Creature> _) => Task.CompletedTask);
        idle.FollowUpState = idle;
        return idle;
    }

    /// <summary>His three zones stand in a row in front of him.</summary>
    public static void PlaceZones()
    {
        if (FmField.Duelist is { } p) FmDuelLayout.Duel(p);
    }

    /// <summary>
    /// What he can see of your monsters. Your face-down cards are unknown to him, so he goes by a typical card's
    /// strength instead of their real ATK and DEF (he finds out when they flip).
    /// </summary>
    const int Unknown = 1200;
    static int SeenAtk(FmSlot m) => m.FaceDown ? Unknown : m.Atk;
    static int SeenDef(FmSlot m) => m.FaceDown ? Unknown : m.Def;
    static int SeenValue(FmSlot m) => m.Position == FmPosition.Attack ? SeenAtk(m) : SeenDef(m);

    static readonly int[] Usable = { 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 681, 682, 683, 684, 685, 686 };
    /// <summary>His traps set face down (up to 3): they spring on your attacks.</summary>
    static readonly List<int> Traps = new();
    static bool CanUse(int id) => FmData.Get(id).IsMonster || Usable.Contains(id) || FmData.Get(id).Type == FmType.Equip;

    static void BuildDeck()
    {
        if (Current == FmPractice.Duelist) { Deck.Clear(); Deck.AddRange(FmPractice.Deck(FmPractice.Level)); return; }
        int index = Current?.DuelistIndex ?? 7;
        if (!_weights.TryGetValue(index, out var weights)) _weights[index] = weights = ReadWeights(index);
        Deck.Clear();
        var counts = new Dictionary<int, int>();
        int total = weights.Sum();
        const int size = 40; // every duelist brings 40 cards
        for (int guard = 0; Deck.Count < size && guard < 4000 && total > 0; guard++)
        {
            int r = Rng.Next(total), id = 0;
            for (int k = 0; k < weights.Length; k++) { r -= weights[k]; if (r < 0) { id = k + 1; break; } }
            if (id == 0 || !CanUse(id) || id is >= 17 and <= 21 || counts.GetValueOrDefault(id) >= 3) continue;
            counts[id] = counts.GetValueOrDefault(id) + 1;
            Deck.Add(id);
        }
        // a sparse drop table can't fill 40 with 3 copies each: fill up with more copies of what it has
        for (int guard = 0; Deck.Count < size && Deck.Count > 0 && guard < 400; guard++) Deck.Add(Deck[Rng.Next(Deck.Count)]);
        if (Deck.Count < 20) Deck.AddRange(new[] { 2, 3, 6, 9, 24, 30, 46, 58, 61, 75, 103, 114, 130, 151, 160, 172, 180, 191, 210, 242 }); // a basic deck if the table can't be read
    }

    static int[] ReadWeights(int duelistIndex)
    {
        var wa = FmData.Wa;
        var w = new int[FmData.CardCount];
        if (wa == null) return w;
        int o = 0xE9B000 + (duelistIndex - 1) * 0x1800;
        for (int k = 0; k < FmData.CardCount && o + k * 2 + 1 < wa.Length; k++) w[k] = BitConverter.ToUInt16(wa, o + k * 2);
        return w;
    }

    static void Draw()
    {
        while (Hand.Count < PoolSize)
        {
            if (Deck.Count == 0)
            {
                // FM: a duelist who can't draw a full hand has lost (attrition)
                if (Priest is { IsAlive: true } him && !FmDuelStats.Attrition)
                {
                    FmDuelStats.Attrition = true;
                    Banner($"{Name} has no cards left to draw!  You win by attrition!");
                    MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(him, true));
                }
                return;
            }
            int i = Rng.Next(Deck.Count);
            Hand.Add(Deck[i]); Deck.RemoveAt(i);
        }
    }

    // ---------------- battles ----------------
    static async Task Lp(PlayerChoiceContext ctx, Creature who, int amount)
    {
        if (amount <= 0 || who.IsDead) return;
        await FmRules.Raw(() => CreatureCmd.Damage(ctx, who, amount, ValueProp.Unblockable | ValueProp.Unpowered, null, null));
    }

    static async Task DestroyRival(FmSlot s)
    {
        if (s.MonsterId == 0) return;
        FmSfx.Play(FmSfx.Destroy);
        s.Clear();
        FmField.NotifyChanged();
        await Task.Delay(250);
    }

    static async Task DestroyMine(FmSlot s, Player player)
    {
        if (s.Creature(player) is { IsAlive: true } c) await CreatureCmd.Kill(c);
    }

    /// <summary>One FM battle. Returns when it's resolved.</summary>
    static async Task Battle(PlayerChoiceContext ctx, Player player, FmSlot att, FmSlot def, bool playerAttacks)
    {
        // face-down monsters are revealed when they battle
        if (att.FaceDown || def.FaceDown)
        {
            att.FaceDown = def.FaceDown = false;
            FmField.NotifyChanged();
            await Wait(0.35);
        }
        // Guardian Stars: the monster whose star beats the other's gets +500 for this battle
        var (sa, sd, note) = FmStars.Battle(att, def, player);
        if (note != null)
        {
            Banner($"Guardian Star!  {note}");
            FmSfx.Play(FmSfx.PowerUp);
            await Wait(0.6);
        }
        int a = att.Atk + sa;
        Creature attOwner = playerAttacks ? player.Creature : Priest!, defOwner = playerAttacks ? Priest! : player.Creature;
        int defValue = def.Position == FmPosition.Attack ? def.Atk + sd : def.Def + sd, defHp = defOwner.CurrentHp;
        int attId = att.MonsterId, defId = def.MonsterId;
        Task Destroy(FmSlot s) => s.Rival ? DestroyRival(s) : DestroyMine(s, player);
        if (def.Position == FmPosition.Attack)
        {
            int t = def.Atk + sd;
            if (a > t) { await Destroy(def); await Lp(ctx, defOwner, a - t); }
            else if (a < t) { await Destroy(att); await Lp(ctx, attOwner, t - a); }
            else { await Destroy(def); await Destroy(att); }
        }
        else
        {
            int d = def.Def + sd;
            if (a > d) await Destroy(def);
            else if (a < d) await Lp(ctx, attOwner, d - a);
        }
        FmDuelStats.Battle(playerAttacks, a, defValue, def.MonsterId != defId || def.MonsterId == 0, att.MonsterId != attId || att.MonsterId == 0, defOwner.CurrentHp < defHp);
        FmField.NotifyChanged();
    }

    /// <summary>Your monster attacks one of his monsters, or him directly when his field is empty.</summary>
    public static async Task PlayerAttack(PlayerChoiceContext ctx, Player player, FmSlot mine, Creature target)
    {
        if (await SpringHisTrap(mine, player)) return;
        if (SlotOf(target) is { MonsterId: not 0 } theirs) await Battle(ctx, player, mine, theirs, true);
        else if (target == Priest && !HasMonster) { FmDuelStats.DirectAttack(true, mine.Atk); await Lp(ctx, Priest, mine.Atk); }
    }

    /// <summary>Raigeki / Dark Hole on his side: his monsters are destroyed, he takes no damage.</summary>
    public static async Task DestroyAll()
    {
        foreach (var s in Slots.Where(s => s.MonsterId != 0)) await DestroyRival(s);
    }

    // ---------------- his turn ----------------
    /// <summary>He picks these cards from his face-down hand with the cursor and plays them (they leave his hand).</summary>
    static async Task UseCards(IReadOnlyList<int> ids, Vector2? to = null)
    {
        // the real cards come out of his hidden pool...
        var used = new List<int>();
        foreach (var id in ids)
        {
            int i = Enumerable.Range(0, Hand.Count).FirstOrDefault(k => Hand[k] == id && !used.Contains(k), -1);
            if (i >= 0) used.Add(i);
        }
        if (used.Count == 0) return;
        FmDuelStats.Com.CardsUsed += used.Count;
        foreach (var i in used.OrderByDescending(i => i)) Hand.RemoveAt(i);
        // ...while the cursor picks that many of the 5 face-down cards on show
        var shown = Enumerable.Range(0, _visible).OrderBy(_ => Rng.Next()).Take(used.Count).OrderBy(i => i).ToList();
        if (shown.Count == 0) return;
        try { await FmRivalHand.Pick(shown); await FmRivalHand.Play(shown, to); } catch (Exception ex) { Log.Warn("[FMDuelist] rival hand: " + ex.Message); }
        _visible -= shown.Count;
    }

    static Task UseCard(int id, Vector2? to = null) => UseCards(new[] { id }, to);

    static FmSpellCard? Spell(int id) => ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))) as FmSpellCard;

    public static async Task TakeTurn()
    {
        var player = FmField.Duelist;
        var combat = Priest?.CombatState;
        if (!Active || Priest == null || Priest.IsDead || player == null || combat == null) return;
        var ctx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
        if (Current == FmAct1Story.Heishin && !FmRestDuel.Active && FmAct1Story.TurnsIntoDuel(combat) >= 12 && player.Creature.CurrentHp > 0)
        {
            Banner($"{Name} unleashes the forbidden power of darkness!");
            try { await FmSpellVfx.DarkHole(); } catch { }
            await Lp(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), player.Creature, player.Creature.CurrentHp);
            return;
        }
        if (Bound)
        {
            Bound = false;
            Banner($"{Name} is bound by Spellbinding Circle and can't move!");
            await Wait(0.8);
            return;
        }
        try
        {
            Draw();
            _visible = Math.Min(HandSize, Hand.Count);
            FmRivalHand.SetCount(_visible);
            foreach (var s in Slots) s.AttackedThisTurn = false;
            await FmRivalHand.BeginTurn(); // your hand slides away, his slides in
            await FmRivalHand.Think();

            // he plays as many cards as he likes, like you: spells, a summon, equips and traps
            await PlayTurn(ctx, player);

            if (FmField.SwordsTurns <= 0) await Attacks(ctx, player);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] rival turn: " + ex); }
        finally
        {
            // his hand slides away (he keeps it) and yours comes back
            try { await FmRivalHand.EndTurn(); } catch (Exception ex) { Log.Warn("[FMDuelist] rival hand end: " + ex.Message); }
        }
    }

    static string Name => Current?.Name ?? "The duelist";

    public static void ApplyLoc(MegaCrit.Sts2.Core.Localization.LocManager loc)
    {
        try
        {
            var monsters = new Dictionary<string, string>
            {
                ["KIN_PRIEST.name"] = Bosses[0].Name,
                ["KIN_PRIEST.followersDeathLine"] = "Impossible... my monsters!",
                ["KIN_PRIEST.moves.RITUAL.speakLine1"] = "[jitter]I summon...![/jitter]",
                ["KNOWLEDGE_DEMON.name"] = Bosses[1].Name,
                ["QUEEN.name"] = Bosses[2].Name,
            };
            foreach (var t in new[] { typeof(FmRivalZone1), typeof(FmRivalZone2), typeof(FmRivalZone3), typeof(FmRivalZone4), typeof(FmRivalZone5) })
                monsters[ModelDb.GetEntry(t) + ".name"] = "Monster Zone";
            // your monster zone 1 is the game's Osty underneath: never named as Osty
            monsters["OSTY.name"] = "Monster Zone";
            foreach (var t in new[] { typeof(FmZonePet2), typeof(FmZonePet3), typeof(FmZonePet4), typeof(FmZonePet5) })
                monsters[ModelDb.GetEntry(t) + ".name"] = "Monster Zone";
            loc.GetTable("monsters").MergeWith(monsters);
            loc.GetTable("encounters").MergeWith(new Dictionary<string, string>
            {
                ["THE_KIN_BOSS.title"] = Bosses[0].Name,
                ["THE_KIN_BOSS.loss"] = "{character} was defeated in a duel by [gold]{encounter}[/gold].",
                ["KNOWLEDGE_DEMON_BOSS.title"] = Bosses[1].Name,
                ["KNOWLEDGE_DEMON_BOSS.loss"] = "{character} was defeated in a duel by the [gold]{encounter}[/gold].",
                ["QUEEN_BOSS.title"] = Bosses[2].Name,
                ["QUEEN_BOSS.loss"] = "{character} was defeated in a duel by [gold]{encounter}[/gold].",
            });
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] rival text: " + ex.Message); }
    }

    /// <summary>His best summon: a lone monster, a fusion from his hand, or a fusion onto one of his monsters.</summary>
    /// <summary>
    /// His turn's plays, in order (he can make all of them in one turn, just like you):
    ///   heal when hurt; Raigeki / Dark Hole when your field outclasses his; burn spells; his best monster or fusion
    ///   (one summon per turn, like yours); equips that help (winning equips first); traps set face down (up to 3).
    /// </summary>
    static async Task PlayTurn(PlayerChoiceContext ctx, Player player)
    {
        var mine = FmField.Living(player).ToList();
        var skill = Me;
        bool spells = Roll(skill.Spell);

        // heal when hurt
        if (spells && Priest!.CurrentHp <= LifePoints * 0.55 && Hand.FirstOrDefault(id => Spell(id)?.Kind == FmSpellKind.Heal) is int heal and > 0)
        {
            await UseCard(heal);
            Banner($"{Name} plays {FmData.Get(heal).Name}!");
            await FmHealVfx.Play(heal, Priest);
            await FmRules.Raw(() => CreatureCmd.Heal(Priest, Spell(heal)!.Value));
            await Wait(0.6);
        }
        // Raigeki: wipe your field when you're ahead on monsters
        mine = FmField.Living(player).ToList();
        if (spells && !MustAnswer && Hand.Contains(337) && mine.Count > 0 && (mine.Count >= 2 || mine.Max(m => SeenAtk(m)) > (Living.Any() ? Living.Max(m => m.Atk) : 0)))
        {
            await UseCard(337);
            Banner($"{Name} plays Raigeki!");
            try { await FmSpellVfx.Raigeki(mine.Select(m => m.Creature(player)).OfType<Creature>()); } catch { }
            foreach (var m in mine) await DestroyMine(m, player);
            await Wait(0.5);
        }
        // Dark Hole only when your field is clearly stronger than his
        mine = FmField.Living(player).ToList();
        if (spells && !MustAnswer && Hand.Contains(336) && mine.Count > 0 && mine.Sum(m => SeenAtk(m)) > Living.Sum(m => m.Atk) + 1000)
        {
            await UseCard(336);
            Banner($"{Name} plays Dark Hole!");
            try { await FmSpellVfx.DarkHole(); } catch { }
            foreach (var m in mine) await DestroyMine(m, player);
            await DestroyAll();
            await Wait(0.5);
        }
        // burn spells go straight at your Life Points
        foreach (var burn in Hand.Where(id => spells && Spell(id)?.Kind is FmSpellKind.Burn or FmSpellKind.BurnSelfDmg).ToList())
        {
            await UseCard(burn);
            Banner($"{Name} plays {FmData.Get(burn).Name}!");
            await Lp(ctx, player.Creature, Spell(burn)!.Value);
            await Wait(0.4);
        }
        // one summon (his strongest monster or fusion)
        if (BestSummon() is { } plan) await Summon(player, plan);
        // equips: the ones that win a battle first, then any that fit
        if (Roll(skill.Equip))
        {
            while (await Equip(player, needWin: true)) { }
            while (await Equip(player, needWin: false)) { }
        }
        // traps face down
        if (Roll(skill.Trap)) while (await SetTrap()) { }
    }

    /// <summary>His strongest summon from hand: a single monster, a 2-3 card fusion, or a fusion onto his field.</summary>
    /// <summary>
    /// FM: when you have a face-up monster and he has none, he always answers with a single monster (no fusion,
    /// and no field-clearing spell first).
    /// </summary>
    static bool MustAnswer => !Living.Any() && FmField.Duelist is { } p && FmField.Living(p).Any(m => !m.FaceDown);

    static int _emptyChecks, _emptyLimit = 2;

    /// <summary>
    /// Act 1's duelists, when you have no monsters, play a single monster instead of fusing: the first 2 or 3 times
    /// (Young Priest Seto only the first time), then they have a 50% chance to fuse anyway.
    /// </summary>
    static bool EmptyFieldAnswer()
    {
        if (FmField.Duelist is not { } you || FmField.Living(you).Any()) return false;
        bool act1 = Current == Bosses[0] || FmStoryAct1.ForMonster(Current?.Monster) != null;
        if (!act1) return false;
        _emptyChecks++;
        int limit = Current == Bosses[0] ? 1 : _emptyLimit;
        return _emptyChecks <= limit || Rng.NextDouble() < 0.5;
    }

    static (int atk, int result, List<int> chain, FmSlot? onto)? BestSummon()
    {
        var monsters = Hand.Where(id => FmData.Get(id).IsMonster).ToList();
        var skill = Me;
        bool fuse = Roll(skill.Fuse) && !MustAnswer && !EmptyFieldAnswer();
        // a weaker duelist sometimes just plays whatever monster he picks up
        if (!fuse && monsters.Count > 0 && !Roll(skill.Best))
        {
            int pick = monsters[Rng.Next(monsters.Count)];
            return (FmData.Get(pick).Atk, pick, new List<int> { pick }, null);
        }
        int bestAtk = -1, bestResult = 0; List<int> bestChain = new(); FmSlot? onto = null;
        void Consider(List<int> chain, int result, FmSlot? field)
        {
            int atk = FmData.Get(result).Atk;
            if (atk > bestAtk || (atk == bestAtk && chain.Count < bestChain.Count)) { bestAtk = atk; bestResult = result; bestChain = chain; onto = field; }
        }
        foreach (var m in monsters) Consider(new List<int> { m }, m, null);
        for (int i = 0; i < monsters.Count; i++)
            for (int j = 0; j < monsters.Count; j++)
            {
                if (!fuse || i == j || FmData.Fuse(monsters[i], monsters[j]) is not int r || !FmData.Get(r).IsMonster) continue;
                Consider(new List<int> { monsters[i], monsters[j] }, r, null);
                for (int k = 0; k < monsters.Count; k++)
                    if (skill.Chain >= 3 && k != i && k != j && FmData.Fuse(r, monsters[k]) is int r2 && FmData.Get(r2).IsMonster)
                        Consider(new List<int> { monsters[i], monsters[j], monsters[k] }, r2, null);
            }
        foreach (var f in Living.Where(_ => fuse && skill.Onto))
            foreach (var m in monsters)
                if (FmData.Fuse(f.MonsterId, m) is int r && FmData.Get(r).IsMonster && FmData.Get(r).Atk > f.Atk)
                    Consider(new List<int> { f.MonsterId, m }, r, f);
        return bestResult == 0 ? null : (bestAtk, bestResult, bestChain, onto);
    }

    static async Task<bool> Summon(Player player, (int atk, int result, List<int> chain, FmSlot? onto) plan)
    {
        var (bestAtk, bestResult, bestChain, onto) = plan;


        var zone = onto ?? Slots.FirstOrDefault(s => s.MonsterId == 0);
        if (zone == null)
        {
            var weakest = Living.OrderBy(s => s.Atk).First();
            if (weakest.Atk >= bestAtk) return false;
            zone = weakest;
        }
        // he picks the cards from his hand with the cursor (a monster already on his field isn't in his hand)
        await UseCards(onto != null ? bestChain.Skip(1).ToList() : bestChain, zone.Visual.CardCenter());
        // play the fusion like yours
        if (bestChain.Count > 1)
        {
            var steps = new List<FmFusionStep>();
            int cur = bestChain[0];
            for (int k = 1; k < bestChain.Count; k++)
            {
                int r = FmData.Fuse(cur, bestChain[k]) ?? bestChain[k];
                steps.Add(new FmFusionStep(cur, bestChain[k], r));
                cur = r;
            }
            Banner($"{Name} fuses {string.Join(" + ", bestChain.Select(c => FmData.Get(c).Name))}!");
            try { await FmFusionAnim.Play(steps, bestChain[0]); } catch { }
        }
        var mine = FmField.Living(player).ToList();
        var info = FmData.Get(bestResult);
        int myBest = mine.Count == 0 ? 0 : mine.Max(SeenValue);
        bool fused = bestChain.Count > 1;
        zone.Clear();
        zone.MonsterId = bestResult;
        // FM: if it can't beat anything of yours it goes into Defense; it's set face down unless a fusion revealed it
        zone.Position = mine.Count == 0 || info.Atk > myBest || info.Atk >= info.Def ? FmPosition.Attack : FmPosition.Defense;
        zone.FaceDown = !fused;
        zone.Star = FmStars.ForRival(bestResult, FmField.Living(player));
        if (zone.FaceDown) FmDuelStats.ComFaceDown++;
        zone.Visual.Attach(player);
        FmField.NotifyChanged();
        Banner(zone.FaceDown
            ? $"{Name} sets a monster face down in {(zone.Position == FmPosition.Attack ? "Attack" : "Defense")} position."
            : $"{Name} summons {info.Name} ({info.Atk}/{info.Def}) in {(zone.Position == FmPosition.Attack ? "Attack" : "Defense")} position!");
        await Wait(0.9);
        return true;
    }

    static async Task<bool> Equip(Player player, bool needWin)
    {
        var mine = FmField.Living(player).ToList();
        int myTop = mine.Count == 0 ? 0 : mine.Max(SeenValue);
        foreach (var id in Hand.Where(id => FmData.Get(id).Type == FmType.Equip).ToList())
        {
            if (Spell(id) is not { } sp) continue;
            var target = Living.Where(s => FmData.CanEquip(id, s.MonsterId))
                .Where(s => !needWin || (s.Atk <= myTop && s.Atk + sp.Value > myTop))
                .OrderByDescending(s => s.Atk).FirstOrDefault();
            if (target == null) continue;
            await UseCard(id, target.Visual.CardCenter());
            Banner(target.FaceDown ? $"{Name} equips {FmData.Get(id).Name} to a face-down monster!" : $"{Name} equips {FmData.Get(id).Name} to {FmData.Get(target.MonsterId).Name}!");
            try { await target.Visual.EquipFx(id, sp.Value); } catch { }
            target.EquipList.Add((id, sp.Value));
            FmField.NotifyChanged();
            return true;
        }
        return false;
    }

    /// <summary>He sets the traps in his hand face down (up to 3 on the field).</summary>
    static async Task<bool> SetTrap()
    {
        if (Traps.Count >= FmField.MaxTraps || Hand.FirstOrDefault(id => FmData.Get(id).Type == FmType.Trap) is var id && id == 0) return false;
        await UseCard(id);
        Traps.Add(id);
        FmRivalHand.SetTraps(Traps.Count);
        Banner($"{Name} sets a card face down.");
        await Wait(0.4);
        return true;
    }

    /// <summary>One of his face-down traps springs on your attacking monster, if one fits. Returns true if your attack was stopped.</summary>
    static async Task<bool> SpringHisTrap(FmSlot attacker, Player player)
    {
        for (int i = 0; i < Traps.Count; i++)
        {
            int id = Traps[i];
            if (!FmSheet.Traps.TryGetValue(id, out var t)) continue;
            bool hits = t.kind == FmTrapKind.DestroyAny || (t.kind == FmTrapKind.DestroyLe && attacker.Atk <= t.value);
            if (!hits) continue;
            Traps.RemoveAt(i);
            FmRivalHand.SetTraps(Traps.Count);
            FmSfx.Play(FmSfx.Trap);
            Banner($"Trap card! {Name} springs {FmData.Get(id).Name} on {FmData.Get(attacker.MonsterId).Name}!");
            await Wait(0.4);
            await DestroyMine(attacker, player);
            return true;
        }
        return false;
    }

    /// <summary>He only attacks battles he wins (or trades when your monster is the bigger threat), and hits you directly when your field is empty.</summary>
    /// <summary>Can this monster of his win a battle against one of yours (or is your field empty)?</summary>
    static bool HasPrey(FmSlot s, Player player)
    {
        var mine = FmField.Living(player).ToList();
        return mine.Count == 0 || mine.Any(m => SeenValue(m) < s.Atk);
    }

    /// <summary>FM AI: a monster of his switches position, with a banner.</summary>
    static async Task Switch(FmSlot s, FmPosition to)
    {
        if (s.Position == to) return;
        s.Position = to;
        FmSfx.Play(FmSfx.Flip);
        FmField.NotifyChanged();
        Banner(s.FaceDown ? $"A face-down monster switches to {(to == FmPosition.Attack ? "Attack" : "Defense")} position!" : $"{FmData.Get(s.MonsterId).Name} switches to {(to == FmPosition.Attack ? "Attack" : "Defense")} position!");
        await Wait(0.35);
    }

    static async Task Attacks(PlayerChoiceContext ctx, Player player)
    {
        // monsters in Defense that now have something to beat stand up to attack
        foreach (var s in Living.Where(s => s.Position == FmPosition.Defense && !s.AttackedThisTurn).ToList())
            if (HasPrey(s, player)) await Switch(s, FmPosition.Attack);

        foreach (var s in Living.Where(s => s.Position == FmPosition.Attack).OrderByDescending(s => s.Atk).ToList())
        {
            // FM AI: once his strongest remaining monster has nothing it can beat, every monster of his that hasn't
            // attacked goes into Defense Position
            if (s.MonsterId != 0 && !s.AttackedThisTurn && !HasPrey(s, player))
            {
                foreach (var d in Living.Where(d => !d.AttackedThisTurn && d.Position == FmPosition.Attack).ToList()) await Switch(d, FmPosition.Defense);
                break;
            }

            if (!Active || Priest == null || Priest.IsDead || player.Creature.IsDead || player.Creature.CurrentHp <= 0 || s.MonsterId == 0) break;
            var mine = FmField.Living(player).ToList();
            FmSlot? target = null; double best = 0;
            foreach (var m in mine)
            {
                double v = m.Position == FmPosition.Attack
                    ? (s.Atk > SeenAtk(m) ? 1000 + (s.Atk - SeenAtk(m)) + SeenAtk(m) : s.Atk == SeenAtk(m) ? 200 : -1)
                    : (s.Atk > SeenDef(m) ? 600 + SeenAtk(m) : -1);
                if (v > best) { best = v; target = m; }
            }
            if (mine.Count > 0 && target == null) continue; // no battle worth fighting
            var from = s.Visual.CardCenter();
            // your traps spring on his attack
            if (SpringTrap(s, player)) { await DestroyRival(s); continue; }
            if (s.FaceDown) { s.FaceDown = false; FmField.NotifyChanged(); await Wait(0.35); }
            if (target == null)
            {
                Banner($"{FmData.Get(s.MonsterId).Name} attacks you directly!");
                var to = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.Hitbox.GetGlobalRect().GetCenter() ?? from;
                await FmAttackVfx.Play(s.MonsterId, from, to);
                FmDuelStats.DirectAttack(false, s.Atk);
                await Lp(ctx, player.Creature, s.Atk);
            }
            else
            {
                Banner($"{FmData.Get(s.MonsterId).Name} attacks {FmData.Get(target.MonsterId).Name}!");
                await FmAttackVfx.Play(s.MonsterId, from, target.Visual.CardCenter());
                await Battle(ctx, player, s, target, false);
            }
            s.AttackedThisTurn = true;
            await Wait(0.5);
        }
    }

    static bool SpringTrap(FmSlot attacker, Player player)
    {
        for (int i = 0; i < FmField.Traps.Count; i++)
        {
            int id = FmField.Traps[i];
            if (!FmSheet.Traps.TryGetValue(id, out var t)) continue;
            bool hits = t.kind == FmTrapKind.DestroyAny || (t.kind == FmTrapKind.DestroyLe && attacker.Atk <= t.value);
            if (!hits) continue;
            FmField.Traps.RemoveAt(i);
            FmDuelStats.Traps++;
            FmSfx.Play(FmSfx.Trap);
            Banner($"Your {FmData.Get(id).Name} destroys {FmData.Get(attacker.MonsterId).Name}!");
            FmField.NotifyChanged();
            return true;
        }
        return false;
    }

    static async Task Wait(double s)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var t = tree.CreateTimer(s);
        await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>A line of duel commentary at the top of the screen.</summary>
    /// <summary>FM duel rule: whoever goes first can't attack on that first turn.</summary>
    public static bool FirstTurnNoAttack => Active && Priest?.CombatState is { } cs && cs.RoundNumber <= 1;

    static int _firstTurnWarned = -1;

    /// <summary>True (with a reminder) if attacking is blocked by the first-turn rule.</summary>
    public static bool BlockFirstTurnAttack()
    {
        if (!FirstTurnNoAttack) return false;
        if (Priest!.CombatState is { })
        {
            _firstTurnWarned = Priest.CombatState.RoundNumber;
            Banner("You go first: no attacking on the first turn of a duel!");
            FmSfx.Play(FmSfx.Flip);
        }
        return true;
    }

    static CanvasLayer? _bannerLayer;

    /// <summary>Clears the commentary line at once (leaving a run).</summary>
    public static void ClearBanner()
    {
        if (_bannerLayer != null && GodotObject.IsInstanceValid(_bannerLayer)) _bannerLayer.QueueFree();
        _bannerLayer = null; _bannerLabel = null;
    }
    static Label? _bannerLabel;
    static Tween? _bannerTween;

    /// <summary>
    /// The gold commentary line under his face-down hand. Only one shows at a time: a new line first fades the old one
    /// out quickly, then fades in.
    /// </summary>
    public static void Banner(string text)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || NCombatRoom.Instance is not { } room) return;
        Log.Info("[FMDuelist] Duel: " + text);
        float delay = 0f;
        if (_bannerLabel != null && GodotObject.IsInstanceValid(_bannerLabel))
        {
            var old = _bannerLabel; var oldLayer = _bannerLayer;
            if (old.Modulate.A > 0.01f)
            {
                delay = 0.12f;
                if (_bannerTween != null && _bannerTween.IsValid()) _bannerTween.Kill(); // stop its own fade-in / hold
                var fade = old.CreateTween();
                fade.TweenProperty(old, "modulate:a", 0f, delay);
                fade.TweenCallback(Callable.From(() => { if (oldLayer != null && GodotObject.IsInstanceValid(oldLayer)) oldLayer.QueueFree(); }));
            }
            else
            {
                if (_bannerTween != null && _bannerTween.IsValid()) _bannerTween.Kill();
                if (oldLayer != null && GodotObject.IsInstanceValid(oldLayer)) oldLayer.QueueFree();
            }
        }
        var layer = new CanvasLayer { Layer = 75 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(view.X, 60), Position = new Vector2(0, 255), Modulate = new Color(1, 1, 1, 0) };
        label.AddThemeFontSizeOverride("font_size", 32);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        label.AddThemeConstantOverride("outline_size", 10);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer.AddChild(label);
        var tw = label.CreateTween();
        if (delay > 0) tw.TweenInterval(delay);
        tw.TweenProperty(label, "modulate:a", 1f, 0.15);
        tw.TweenInterval(1.4);
        tw.TweenProperty(label, "modulate:a", 0f, 0.3);
        tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }));
        _bannerLayer = layer; _bannerLabel = label; _bannerTween = tw;
    }
}

/// <summary>The boss duelist's monster zones: invisible enemy stand-ins (Osty's skeleton, hidden) under his cards.</summary>
public abstract class FmRivalZone : MonsterModel
{
    public override int MinInitialHp => 1;
    public override int MaxInitialHp => 1;
    public override bool IsHealthBarVisible => false;
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/osty");

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var idle = new MoveState("NOTHING_MOVE", (IReadOnlyList<Creature> _) => Task.CompletedTask);
        idle.FollowUpState = idle;
        return new MonsterMoveStateMachine(new List<MonsterState> { idle }, idle);
    }

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        var idle = new AnimState("idle_loop", isLooping: true);
        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState("Dead", new AnimState("die"));
        animator.AddAnyState("Hit", new AnimState("hurt"));
        return animator;
    }
}

public sealed class FmRivalZone1 : FmRivalZone { }
public sealed class FmRivalZone2 : FmRivalZone { }
public sealed class FmRivalZone3 : FmRivalZone { }
public sealed class FmRivalZone4 : FmRivalZone { }
public sealed class FmRivalZone5 : FmRivalZone { }

// ---------------- hooks into the game ----------------

/// <summary>Each act's boss is its duelist in FM runs (Act 1 The Kin, Act 2 Knowledge Demon, Act 3 Queen).</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Runs.RunManager), nameof(MegaCrit.Sts2.Core.Runs.RunManager.GenerateRooms))]
static class Act1BossPatch
{
    static void Postfix(MegaCrit.Sts2.Core.Runs.RunManager __instance)
    {
        try
        {
            var state = Traverse.Create(__instance).Property("State").GetValue<RunState>();
            if (state == null || !state.Players.Any(p => p.Character is MegaCrit.Sts2.Core.Models.Characters.Necrobinder) || state.Acts.Count == 0) return;
            foreach (var b in FmRival.Bosses)
                if (b.Act < state.Acts.Count)
                    state.Acts[b.Act].SetBossEncounter((EncounterModel)AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Encounter)).MakeGenericMethod(b.Encounter).Invoke(null, null)!); // the canonical model: the room makes its own copy
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] act 1 boss: " + ex.Message); }
    }
}

/// <summary>No minions: a boss duelist brings monster zones instead.</summary>
[HarmonyPatch]
static class BossNoMinionsPatch
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        FmRival.Bosses.Select(b => AccessTools.Method(b.Encounter, "GenerateMonsters")).Where(m => m != null);

    static void Postfix(MethodBase __originalMethod, ref IReadOnlyList<(MonsterModel, string?)> __result)
    {
        if (!FmRules.RunActive) return;
        var boss = FmRival.Bosses.First(b => b.Encounter == __originalMethod.DeclaringType);
        __result = __result.Where(m => m.Item1.GetType() == boss.Monster).ToList();
    }
}

/// <summary>The priest duels instead of using his own moves, and shows no intents.</summary>
[HarmonyPatch]
static class KinPriestTurnPatch
{
    [HarmonyPrefix, HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
    static bool Move(MonsterModel __instance, ref Task __result)
    {
        if (!FmRival.Active || __instance.Creature != FmRival.Priest) return true;
        __result = FmRival.TakeTurn();
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.RollMove))]
    static void NoIntent(MonsterModel __instance)
    {
        if (FmRival.Active && __instance.Creature == FmRival.Priest)
        {
            var idle = new MoveState("FM_DUEL_MOVE", (IReadOnlyList<Creature> _) => Task.CompletedTask);
            idle.FollowUpState = idle;
            __instance.SetMoveImmediate(idle, true);
        }
    }
}

/// <summary>
/// Only the three duelists are ever bosses in FM runs, including runs saved earlier and the Ascension double-boss:
/// whenever an act's boss is looked up, it's set to that act's duelist first.
/// </summary>
[HarmonyPatch]
static class OnlyDuelistBossesPatch
{
    static FmRival.Duelist? For(ActModel act) => act switch
    {
        MegaCrit.Sts2.Core.Models.Acts.Overgrowth or MegaCrit.Sts2.Core.Models.Acts.Underdocks => FmRival.Bosses[0],
        MegaCrit.Sts2.Core.Models.Acts.Hive => FmRival.Bosses[1],
        MegaCrit.Sts2.Core.Models.Acts.Glory => FmRival.Bosses[2],
        _ => null,
    };

    // the canonical model: entering the room makes its own copy
    static EncounterModel Make(FmRival.Duelist d) =>
        (EncounterModel)AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Encounter)).MakeGenericMethod(d.Encounter).Invoke(null, null)!;

    [HarmonyPostfix, HarmonyPatch(typeof(ActModel), nameof(ActModel.BossEncounter), MethodType.Getter)]
    static void Boss(ActModel __instance, ref EncounterModel __result)
    {
        try
        {
            if (!FmRules.RunActive || For(__instance) is not { } d || __result?.GetType() == d.Encounter) return;
            __instance.SetBossEncounter(Make(d));
            __result = __instance.BossEncounter;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] boss lookup: " + ex.Message); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ActModel), nameof(ActModel.SecondBossEncounter), MethodType.Getter)]
    static void SecondBoss(ActModel __instance, ref EncounterModel? __result)
    {
        try
        {
            if (!FmRules.RunActive || __result == null || For(__instance) is not { } d || __result.GetType() == d.Encounter) return;
            __instance.SetSecondBossEncounter(Make(d));
            __result = __instance.SecondBossEncounter;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] second boss lookup: " + ex.Message); }
    }
}
