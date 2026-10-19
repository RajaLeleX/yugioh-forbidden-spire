using FMDuelist.Cards;
using FMDuelist.Data;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace FMDuelist.Game;

/// <summary>
/// Wild monsters: between duels you run into a rogue Forbidden Memories monster on its own (or a small pack), like a
/// wild Pokémon. "A wild KURIBOH appeared!", its card picture stands in the room, its HP is 10x its DEF and it hits
/// for its ATK, each with a move pattern of its own (Kuriboh multiplies until there are ten of them). Winning lets
/// you pick 1 of 3 cards from the drop tables of every duelist in the act.
/// </summary>
public static class FmWild
{
    public const int MaxKuribohs = 10;

    /// <summary>The FM card number for a monster's name (from the disc), 0 when it isn't there.</summary>
    public static int Id(string name)
    {
        for (int i = 1; i <= FmData.CardCount; i++) if (string.Equals(FmData.Get(i).Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    // ---------------- the act's encounters ----------------
    public static readonly (Type Encounter, int Act)[] Encounters =
    {
        (typeof(FmWildEncKuriboh), 0), (typeof(FmWildEncGiant), 0), (typeof(FmWildEncSkulls), 0), (typeof(FmWildEncFangs), 0), (typeof(FmWildEncElf), 0), (typeof(FmWildEncPetit), 0),
        (typeof(FmWildEncKuriboh), 1), (typeof(FmWildEncCeltic), 1), (typeof(FmWildEncFangs), 1), (typeof(FmWildEncElf), 1), (typeof(FmWildEncMammoth), 1), (typeof(FmWildEncGiant), 1),
        (typeof(FmWildEncSkull), 2), (typeof(FmWildEncCeltic), 2), (typeof(FmWildEncMammoth), 2), (typeof(FmWildEncKuriboh), 2), (typeof(FmWildEncElf), 2),
    };

    public static List<EncounterModel> For(int act, int count, Random rng)
    {
        var pool = Encounters.Where(e => e.Act == Math.Clamp(act, 0, 2)).Select(e => e.Encounter).ToList();
        var list = new List<EncounterModel>();
        Type? last = null;
        for (int i = 0; i < count; i++)
        {
            var pick = pool.Where(t => t != last).OrderBy(_ => rng.Next()).First();
            last = pick;
            list.Add((EncounterModel)AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Encounter)).MakeGenericMethod(pick).Invoke(null, null)!);
        }
        return list;
    }

    // ---------------- the room: card pictures, positions, the banner ----------------
    static readonly HashSet<int> _announced = new();

    public static void Dress(Creature c)
    {
        try
        {
            if (c.Monster is not FmWildMonster m || NCombatRoom.Instance?.GetCreatureNode(c) is not { } node) return;
            if (node.Visuals.GetCurrentBody() is { } body) body.Visible = false;
            if (node.Visuals.GetNodeOrNull("FmWildArt") != null) return;
            var holder = new Node2D { Name = "FmWildArt" };
            node.Visuals.AddChild(holder);
            if (FmData.Art(m.CardId) is { } art)
            {
                float h = m.ArtHeight, k = h / art.GetHeight(), w = art.GetWidth() * k;
                holder.AddChild(new Polygon2D { Color = new Color(0, 0, 0, 0.55f), Polygon = Ellipse(w * 0.55f, 16), Position = new Vector2(0, 4) });
                var bodyNode = new Node2D();
                holder.AddChild(bodyNode);
                bodyNode.AddChild(new Sprite2D { Texture = art, Scale = new Vector2(k, k), Position = new Vector2(0, -h / 2 - 6), TextureFilter = CanvasItem.TextureFilterEnum.Linear });
                bodyNode.AddChild(new Line2D
                {
                    Width = 4, DefaultColor = new Color(0.95f, 0.82f, 0.4f), Closed = true,
                    Points = new[] { new Vector2(-w / 2, -h - 6), new Vector2(w / 2, -h - 6), new Vector2(w / 2, -6), new Vector2(-w / 2, -6) },
                });
                var t = bodyNode.CreateTween().SetLoops();
                float phase = (float)(c.GetHashCode() % 7) * 0.05f;
                t.TweenInterval(phase);
                t.TweenProperty(bodyNode, "position:y", -8f, 0.85).SetTrans(Tween.TransitionType.Sine);
                t.TweenProperty(bodyNode, "position:y", 0f, 0.85).SetTrans(Tween.TransitionType.Sine);
            }
            holder.Modulate = new Color(1, 1, 1, 0);
            var fade = holder.CreateTween(); fade.TweenProperty(holder, "modulate:a", 1f, 0.35);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] wild dress: " + ex.Message); }
    }

    /// <summary>The wild monsters stand in up to two rows on the right, the newest at the back.</summary>
    public static void Layout(ICombatState? combat)
    {
        try
        {
            if (combat == null || NCombatRoom.Instance is not { } room) return;
            var all = combat.Enemies.Where(e => e.IsAlive && e.Monster is FmWildMonster).ToList();
            if (all.Count == 0) return;
            int perRow = all.Count <= 3 ? all.Count : (all.Count + 1) / 2;
            float left = 170, right = 900, span = right - left;
            for (int i = 0; i < all.Count; i++)
            {
                if (room.GetCreatureNode(all[i]) is not { } n) continue;
                int row = i / perRow, col = i % perRow, inRow = Math.Min(perRow, all.Count - row * perRow);
                float x = left + span * (col + 0.5f) / inRow + (row == 1 ? span / perRow * 0.25f : 0);
                var to = new Vector2(x, 210 - row * 120);
                float scale = row == 1 ? 0.82f : 1f;
                var t = n.CreateTween().SetParallel();
                t.TweenProperty(n, "position", to, 0.3).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                t.TweenProperty(n, "scale", new Vector2(scale, scale), 0.3);
                n.ZIndex = row == 1 ? -1 : 0;
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] wild layout: " + ex.Message); }
    }

    public static void Announce(Creature c)
    {
        if (c.CombatState is not { } combat || !_announced.Add(combat.GetHashCode())) return;
        if (_announced.Count > 64) _announced.Clear();
        Callable.From(() => Banner(combat)).CallDeferred();
    }

    static async void Banner(ICombatState combat)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var wild = combat.Enemies.Where(e => e.Monster is FmWildMonster).ToList();
        if (wild.Count == 0) return;
        var name = FmData.Get(((FmWildMonster)wild[0].Monster!).CardId).Name.ToUpperInvariant();
        string text = wild.Count == 1 ? $"A wild {name} appeared!" : $"{wild.Count} wild {name}S appeared!";
        var layer = new CanvasLayer { Layer = 70 };
        tree.Root.AddChild(layer);
        try
        {
            var view = layer.GetViewport().GetVisibleRect().Size;
            float px = Math.Max(2f, view.Y / 300f);
            float w = text.Length * 8 * px + 80, h = 12 * px + 44;
            var box = new Panel { Size = new Vector2(w, h), Position = new Vector2(view.X, view.Y * 0.16f), MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.07f, 0.09f, 0.28f, 0.95f), BorderColor = new Color(0.95f, 0.82f, 0.4f),
                BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4, ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 6,
            });
            layer.AddChild(box);
            var label = FmTextBox.Text(text, px);
            label.Position = new Vector2(40, 22);
            box.AddChild(label);
            FmSfx.Play(FmSfx.Summon);
            var t = box.CreateTween();
            t.TweenProperty(box, "position:x", (view.X - w) / 2, 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            t.TweenInterval(1.6);
            t.TweenProperty(box, "modulate:a", 0f, 0.4);
            await box.ToSignal(t, Tween.SignalName.Finished);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] wild banner: " + ex.Message); }
        finally { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); }
    }

    static Vector2[] Ellipse(float rx, float ry, int n = 24)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; p[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry); }
        return p;
    }

    // ---------------- the reward: 1 of 3 cards from the act's duelists' drops ----------------
    public static bool Rewards(Player player, List<Reward> rewards, AbstractRoom? room)
    {
        if (room is not CombatRoom cr || cr.Encounter is not FmWildEncounter) return false;
        var ids = FmRestDuel.ActPool(player.RunState.CurrentActIndex, 3);
        if (ids.Count == 0) return false;
        rewards.RemoveAll(r => r is CardReward);
        try
        {
            var cards = ids.Select(id => player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player)).ToList();
            var opts = new CardCreationOptions(cards, CardCreationSource.Encounter, CardRarityOddsType.Uniform);
            rewards.Add(new CardReward(cards, CardCreationSource.Encounter, player, opts));
            Log.Info($"[FMDuelist] wild reward: {string.Join(", ", ids.Select(i => FmData.Get(i).Name))}");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] wild reward: " + ex.Message); }
        return true;
    }

    // ---------------- names ----------------
    public static void ApplyLoc(LocManager loc)
    {
        var monsters = new Dictionary<string, string>();
        var encounters = new Dictionary<string, string>();
        foreach (var t in typeof(FmWild).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(FmWildMonster).IsAssignableFrom(t)))
        {
            var m = (FmWildMonster)ModelDb.GetById<MonsterModel>(ModelDb.GetId(t));
            string e = ModelDb.GetEntry(t);
            monsters[e + ".name"] = m.CardName;
            foreach (var (move, title) in m.MoveTitles) monsters[$"{e}.moves.{move}.title"] = title;
        }
        foreach (var t in typeof(FmWild).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(FmWildEncounter).IsAssignableFrom(t)))
        {
            var enc = (FmWildEncounter)ModelDb.GetById<EncounterModel>(ModelDb.GetId(t));
            encounters[ModelDb.GetEntry(t) + ".title"] = "Wild " + enc.Title0;
            encounters[ModelDb.GetEntry(t) + ".loss"] = "Overwhelmed by a wild " + enc.Title0;
        }
        loc.GetTable("monsters").MergeWith(monsters);
        loc.GetTable("encounters").MergeWith(encounters);
    }
}

// =====================================================================================================
// the monsters
// =====================================================================================================

public abstract class FmWildMonster : MonsterModel
{
    public abstract string CardName { get; }
    public virtual float ArtHeight => 250f;
    public abstract IEnumerable<(string move, string title)> MoveTitles { get; }

    int _id = -1;
    public int CardId => _id >= 0 ? _id : (_id = FmWild.Id(CardName));
    FmCardInfo Info => FmData.Get(CardId);
    public int Atk => CardId > 0 ? Info.Atk : 500;
    public int Def => CardId > 0 ? Info.Def : 500;

    // 10x DEF (the Millennium Puzzle leaves wild monsters' HP as it is)
    public override int MinInitialHp => Math.Max(100, Def * 10);
    public override int MaxInitialHp => Math.Max(100, Def * 10);
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/necrobinder");

    /// <summary>A hit for this much FM damage (the Puzzle scales enemy hits by 100).</summary>
    protected decimal Hit(int fm) => fm / (decimal)FmRules.EnemyScale;
    protected SingleAttackIntent AttackIntent(Func<int> fm) => new(() => fm() / (decimal)FmRules.EnemyScale);

    protected Task Attack(int fm, string fx = "vfx/vfx_attack_slash") =>
        DamageCmd.Attack(Hit(fm)).FromMonster(this).WithAttackerAnim("Attack", 0.3f).WithHitFx(fx).Execute(null);

    protected Task Guard(int fm) => CreatureCmd.GainBlock(Creature, fm / (decimal)FmRules.EnemyScale, ValueProp.Move, null);

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        var c = Creature;
        Callable.From(() => { FmWild.Dress(c); FmWild.Layout(c.CombatState); }).CallDeferred();
        FmWild.Announce(c);
    }

    /// <summary>A wild monster fell: the others close ranks once it's gone.</summary>
    public override async Task AfterDeath(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    {
        await base.AfterDeath(choiceContext, creature, wasRemovalPrevented, deathAnimLength);
        if (creature.Monster is not FmWildMonster || creature == Creature || Creature.IsDead) return;
        var combat = Creature.CombatState;
        if (Engine.GetMainLoop() is SceneTree tree) { var t = tree.CreateTimer(Math.Max(0.2f, deathAnimLength)); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }
        // only one survivor does it
        if (combat?.Enemies.FirstOrDefault(e => e.IsAlive && e.Monster is FmWildMonster) == Creature) FmWild.Layout(combat);
    }

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        var idle = new AnimState("idle_loop", isLooping: true);
        var hurt = new AnimState("hurt"); hurt.NextState = idle;
        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState("Dead", new AnimState("die"));
        animator.AddAnyState("Hit", hurt);
        animator.AddAnyState("Attack", hurt);
        return animator;
    }

    protected static MonsterMoveStateMachine Machine(MonsterState start, params MonsterState[] states) => new(states.ToList(), start);
}

/// <summary>Kuriboh: Multiply, then Tackle, again and again; every clone does the same, up to ten Kuribohs.</summary>
public sealed class FmWildKuriboh : FmWildMonster
{
    public override string CardName => "Kuriboh";
    public override float ArtHeight => 190f;
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("MULTIPLY_MOVE", "Multiply"), ("TACKLE_MOVE", "Tackle"), ("SWARM_MOVE", "Swarm") };

    int Count => Creature.CombatState?.Enemies.Count(e => e.IsAlive && e.Monster is FmWildKuriboh) ?? 1;

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var multiply = new MoveState("MULTIPLY_MOVE", Multiply, new SummonIntent());
        var tackle = new MoveState("TACKLE_MOVE", _ => Attack(Atk, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk));
        // ten of them: no more room to multiply, they just swarm you
        var swarm = new MoveState("SWARM_MOVE", _ => Attack(Atk, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk));
        var next = new ConditionalBranchState("KURIBOH_BRANCH");
        next.AddState(multiply, () => Count < FmWild.MaxKuribohs);
        next.AddState(swarm, () => Count >= FmWild.MaxKuribohs);
        multiply.FollowUpState = tackle;
        tackle.FollowUpState = next;
        swarm.FollowUpState = next;
        return Machine(multiply, multiply, tackle, swarm, next);
    }

    async Task Multiply(IReadOnlyList<Creature> _)
    {
        if (Count >= FmWild.MaxKuribohs || Creature.CombatState is not { } combat) return;
        FmSfx.Play(FmSfx.Summon);
        await CreatureCmd.Add<FmWildKuriboh>(combat, null);
    }
}

/// <summary>Hitotsu-Me Giant: Club Smash twice, then winds up (blocking) for a Crushing Blow at double ATK.</summary>
public sealed class FmWildGiant : FmWildMonster
{
    public override string CardName => "Hitotsu-Me Giant";
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("SMASH_MOVE", "Club Smash"), ("WINDUP_MOVE", "Wind Up"), ("CRUSH_MOVE", "Crushing Blow") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var smash = new MoveState("SMASH_MOVE", _ => Attack(Atk, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk));
        var smash2 = new MoveState("SMASH_MOVE", _ => Attack(Atk, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk));
        var windup = new MoveState("WINDUP_MOVE", _ => Guard(Def), new DefendIntent());
        var crush = new MoveState("CRUSH_MOVE", _ => Attack(Atk * 2, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk * 2));
        smash.FollowUpState = smash2; smash2.FollowUpState = windup; windup.FollowUpState = crush; crush.FollowUpState = smash;
        return Machine(smash, smash, smash2, windup, crush);
    }
}

/// <summary>Mystical Elf: Elf Light, then a Prayer that heals every wild monster a tenth of its HP.</summary>
public sealed class FmWildElf : FmWildMonster
{
    public override string CardName => "Mystical Elf";
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("LIGHT_MOVE", "Elf Light"), ("PRAYER_MOVE", "Prayer") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var light = new MoveState("LIGHT_MOVE", _ => Attack(Atk), AttackIntent(() => Atk));
        var prayer = new MoveState("PRAYER_MOVE", Prayer, new HealIntent());
        light.FollowUpState = prayer; prayer.FollowUpState = light;
        return Machine(light, light, prayer);
    }

    async Task Prayer(IReadOnlyList<Creature> _)
    {
        foreach (var e in Creature.CombatState?.Enemies.Where(e => e.IsAlive && e.Monster is FmWildMonster).ToList() ?? new())
            await CreatureCmd.Heal(e, Math.Max(1, e.MaxHp / 10));
    }
}

/// <summary>Skull Servant, a pack of three: Bone Rattle every turn, and a Shield of Bones (block) every third.</summary>
public sealed class FmWildSkullServant : FmWildMonster
{
    public override string CardName => "Skull Servant";
    public override float ArtHeight => 200f;
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("RATTLE_MOVE", "Bone Rattle"), ("BONES_MOVE", "Shield of Bones") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var a = new MoveState("RATTLE_MOVE", _ => Attack(Atk), AttackIntent(() => Atk));
        var b = new MoveState("RATTLE_MOVE", _ => Attack(Atk), AttackIntent(() => Atk));
        var shield = new MoveState("BONES_MOVE", _ => Guard(Def * 2), new DefendIntent());
        a.FollowUpState = b; b.FollowUpState = shield; shield.FollowUpState = a;
        return Machine(a, a, b, shield);
    }
}

/// <summary>Silver Fang, a pair: Howl (the next Bite hits for double), then Bite.</summary>
public sealed class FmWildSilverFang : FmWildMonster
{
    public override string CardName => "Silver Fang";
    public override float ArtHeight => 210f;
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("BITE_MOVE", "Bite"), ("HOWL_MOVE", "Howl"), ("FANG_MOVE", "Savage Fang") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var bite = new MoveState("BITE_MOVE", _ => Attack(Atk), AttackIntent(() => Atk));
        var howl = new MoveState("HOWL_MOVE", _ => { FmSfx.Play(FmSfx.PowerUp); return Task.CompletedTask; }, new BuffIntent());
        var fang = new MoveState("FANG_MOVE", _ => Attack(Atk * 2), AttackIntent(() => Atk * 2));
        bite.FollowUpState = howl; howl.FollowUpState = fang; fang.FollowUpState = bite;
        return Machine(bite, bite, howl, fang);
    }
}

/// <summary>Petit Dragon: Flame Breath, then Roost (block), then two quick Claws.</summary>
public sealed class FmWildPetitDragon : FmWildMonster
{
    public override string CardName => "Petit Dragon";
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("BREATH_MOVE", "Flame Breath"), ("ROOST_MOVE", "Roost"), ("CLAW_MOVE", "Claw Flurry") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var breath = new MoveState("BREATH_MOVE", _ => Attack(Atk + Atk / 2, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk + Atk / 2));
        var roost = new MoveState("ROOST_MOVE", _ => Guard(Def), new DefendIntent());
        var claws = new MoveState("CLAW_MOVE", async _ => { await Attack(Atk / 2); await Attack(Atk / 2); }, new MultiAttackIntent(Math.Max(1, Atk / 2 / FmRules.EnemyScale), 2));
        breath.FollowUpState = roost; roost.FollowUpState = claws; claws.FollowUpState = breath;
        return Machine(breath, breath, roost, claws);
    }
}

/// <summary>Celtic Guardian: Sword Slash, Guard (block), and a Twin Slash.</summary>
public sealed class FmWildCeltic : FmWildMonster
{
    public override string CardName => "Celtic Guardian";
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("SLASH_MOVE", "Sword Slash"), ("GUARD_MOVE", "Elven Guard"), ("TWIN_MOVE", "Twin Slash") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var slash = new MoveState("SLASH_MOVE", _ => Attack(Atk), AttackIntent(() => Atk));
        var guard = new MoveState("GUARD_MOVE", _ => Guard(Def * 2), new DefendIntent());
        var twin = new MoveState("TWIN_MOVE", async _ => { await Attack(Atk * 3 / 4); await Attack(Atk * 3 / 4); }, new MultiAttackIntent(Math.Max(1, Atk * 3 / 4 / FmRules.EnemyScale), 2));
        slash.FollowUpState = guard; guard.FollowUpState = twin; twin.FollowUpState = slash;
        return Machine(slash, slash, guard, twin);
    }
}

/// <summary>Mammoth Graveyard: Trample every turn, and a Stampede at 1.5x ATK every third.</summary>
public sealed class FmWildMammoth : FmWildMonster
{
    public override string CardName => "Mammoth Graveyard";
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("TRAMPLE_MOVE", "Trample"), ("STAMPEDE_MOVE", "Stampede") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var a = new MoveState("TRAMPLE_MOVE", _ => Attack(Atk, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk));
        var b = new MoveState("TRAMPLE_MOVE", _ => Attack(Atk, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk));
        var c = new MoveState("STAMPEDE_MOVE", _ => Attack(Atk * 3 / 2, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk * 3 / 2));
        a.FollowUpState = b; b.FollowUpState = c; c.FollowUpState = a;
        return Machine(a, a, b, c);
    }
}

/// <summary>Summoned Skull: charges (blocking) for a turn, then Lightning Strike at double ATK.</summary>
public sealed class FmWildSummonedSkull : FmWildMonster
{
    public override string CardName => "Summoned Skull";
    public override float ArtHeight => 280f;
    public override IEnumerable<(string, string)> MoveTitles => new[] { ("CLAW_MOVE", "Demon Claw"), ("CHARGE_MOVE", "Dark Charge"), ("LIGHTNING_MOVE", "Lightning Strike") };
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var claw = new MoveState("CLAW_MOVE", _ => Attack(Atk), AttackIntent(() => Atk));
        var charge = new MoveState("CHARGE_MOVE", _ => Guard(Def), new DefendIntent());
        var bolt = new MoveState("LIGHTNING_MOVE", _ => Attack(Atk * 2, "vfx/vfx_attack_blunt"), AttackIntent(() => Atk * 2));
        claw.FollowUpState = charge; charge.FollowUpState = bolt; bolt.FollowUpState = claw;
        return Machine(claw, claw, charge, bolt);
    }
}

// =====================================================================================================
// the encounters
// =====================================================================================================

public abstract class FmWildEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public abstract string Title0 { get; }
}

public abstract class FmWildEnc<T> : FmWildEncounter where T : FmWildMonster
{
    protected virtual int Count => 1;
    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[] { ModelDb.Monster<T>() };
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        Enumerable.Range(0, Count).Select(_ => ((MonsterModel)ModelDb.Monster<T>().ToMutable(), (string?)null)).ToList();
}

public sealed class FmWildEncKuriboh : FmWildEnc<FmWildKuriboh> { public override string Title0 => "Kuriboh"; }
public sealed class FmWildEncGiant : FmWildEnc<FmWildGiant> { public override string Title0 => "Hitotsu-Me Giant"; }
public sealed class FmWildEncElf : FmWildEnc<FmWildElf> { public override string Title0 => "Mystical Elf"; }
public sealed class FmWildEncSkulls : FmWildEnc<FmWildSkullServant> { protected override int Count => 3; public override string Title0 => "Skull Servants"; }
public sealed class FmWildEncFangs : FmWildEnc<FmWildSilverFang> { protected override int Count => 2; public override string Title0 => "Silver Fangs"; }
public sealed class FmWildEncPetit : FmWildEnc<FmWildPetitDragon> { public override string Title0 => "Petit Dragon"; }
public sealed class FmWildEncCeltic : FmWildEnc<FmWildCeltic> { public override string Title0 => "Celtic Guardian"; }
public sealed class FmWildEncMammoth : FmWildEnc<FmWildMammoth> { public override string Title0 => "Mammoth Graveyard"; }
public sealed class FmWildEncSkull : FmWildEnc<FmWildSummonedSkull> { public override string Title0 => "Summoned Skull"; }

// =====================================================================================================
// hooks
// =====================================================================================================

/// <summary>Every act's standard monster rooms are wild monsters in FM runs.</summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
static class FmWildRoomsPatch
{
    static void Postfix(RunManager __instance)
    {
        try
        {
            var state = Traverse.Create(__instance).Property("State").GetValue<RunState>();
            if (state == null || !state.Players.Any(FmRules.IsFmPlayer)) return;
            var rng = new Random();
            for (int a = 0; a < state.Acts.Count; a++)
            {
                if (Traverse.Create(state.Acts[a]).Field("_rooms").GetValue() is not RoomSet rooms) continue;
                int n = Math.Max(rooms.normalEncounters.Count, 6);
                rooms.normalEncounters.Clear();
                rooms.normalEncounters.AddRange(FmWild.For(a, n, rng));
            }
            Log.Info("[FMDuelist] monster rooms: wild FM monsters");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] wild rooms: " + ex.Message); }
    }
}

/// <summary>Runs already under way (their rooms were rolled before): the next standard room is a wild monster too.</summary>
[HarmonyPatch(typeof(RoomSet), nameof(RoomSet.NextNormalEncounter), MethodType.Getter)]
static class FmWildNextPatch
{
    static void Postfix(RoomSet __instance, ref EncounterModel __result)
    {
        try
        {
            if (__result is FmWildEncounter) return;
            var s = RunManager.Instance.DebugOnlyGetState();
            if (s == null || !s.Players.Any(FmRules.IsFmPlayer)) return;
            var pool = FmWild.Encounters.Where(e => e.Act == Math.Clamp(s.CurrentActIndex, 0, 2)).Select(e => e.Encounter).ToList();
            var pick = pool[(__instance.normalEncountersVisited * 5 + s.CurrentActIndex) % pool.Count];
            __result = (EncounterModel)AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Encounter)).MakeGenericMethod(pick).Invoke(null, null)!;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] wild next: " + ex.Message); }
    }
}
