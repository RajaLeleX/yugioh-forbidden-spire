using FMDuelist.Cards;
using FMDuelist.Data;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

// Godot does not register node classes from mod assemblies, so nothing here subclasses Node:
// input comes from a Harmony patch on NGame._Input, per-frame work from SceneTree.ProcessFrame.

[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
static class NGameInputPatch
{
    static bool Prefix(NGame __instance, InputEvent inputEvent)
    {
        try
        {
            if (FmIntro.Playing && (inputEvent is InputEventMouseButton { Pressed: true } || inputEvent is InputEventKey { Pressed: true }))
            {
                FmIntro.Skip();
                __instance.GetViewport().SetInputAsHandled();
                return false;
            }
            if (FmZonePicker.Open) return true; // its own zone buttons
            if (FmDeckEditor.IsOpen)
            {
                if (FmDeckEditor.HandleInput(inputEvent)) { __instance.GetViewport().SetInputAsHandled(); return false; }
                return true;
            }
            if (FmLibraryChoice.Open) return true; // its own buttons
            if (FmLibraryFm.Open)
            {
                if (FmLibraryFm.HandleInput(inputEvent)) { __instance.GetViewport().SetInputAsHandled(); return false; }
                return true;
            }
            if (FmLibrary.Open)
            {
                if (FmLibrary.HandleKey(inputEvent)) { __instance.GetViewport().SetInputAsHandled(); return false; }
                return true; // its search boxes and buttons get everything else
            }
            if (FmTutorial.Open)
            {
                if (inputEvent is not InputEventKey) return true; // the popup's own buttons take the mouse
                FmTutorial.HandleKey(inputEvent);
                __instance.GetViewport().SetInputAsHandled();
                return false;
            }
            if (!FmInput.Handle(inputEvent, __instance.GetViewport())) return true;
            __instance.GetViewport().SetInputAsHandled();
            return false;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] input: " + ex.Message); return true; }
    }
}

/// <summary>Right-click hand cards to mark them for fusion (numbered, glowing, raised); click the monster to attack, right-click it to switch position.</summary>
public static class FmInput
{
    static bool _hooked;
    static readonly Dictionary<CardModel, Control> _marks = new();

    /// <summary>Hook the per-frame update once (called at combat start).</summary>
    public static void Ensure()
    {
        if (_hooked || Engine.GetMainLoop() is not SceneTree tree) return;
        tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(Process));
        _hooked = true;
        Log.Info("[FMDuelist] Input handler ready");
    }

    static CardModel? HoveredCard(NPlayerHand hand, Vector2 mouse)
    {
        if (hand.FocusedHolder?.CardModel is { } m) return m;
        CardModel? found = null;
        foreach (var h in hand.ActiveHolders)
            if (h.Hitbox.GetGlobalRect().HasPoint(mouse) && h.CardModel != null) found = h.CardModel;
        return found;
    }

    public static bool Handle(InputEvent e, Viewport vp)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb) return false;
        if (MegaCrit.Sts2.Core.Nodes.NRun.Instance == null || NCombatRoom.Instance == null) return false;
        if (NTargetManager.Instance is { IsInSelection: true } aiming)
        {
            // your monster's attack arrow: a click on one of the boss's monster cards picks it, wherever its hit area is
            if (mb.ButtonIndex == MouseButton.Left && FmField.Aiming && RivalTargetUnderMouse(vp.GetMousePosition()) is { } pick)
            {
                PickTarget(aiming, pick);
                return true;
            }
            return false;
        }
        if (FmField.Duelist == null && FmField.Marked.Count == 0 && NPlayerHand.Instance == null) return false;
        var mouse = vp.GetMousePosition();
        if (mb.ButtonIndex == MouseButton.Left) return HandleLeftClick(mouse);
        if (mb.ButtonIndex != MouseButton.Right) return false;
        var hand = NPlayerHand.Instance;
        if (hand != null)
        {
            var model = HoveredCard(hand, mouse);
            Log.Info($"[FMDuelist] Right-click on {model?.Id.Entry ?? "nothing"}");
            if (model is FmCardBase || FmJunk.Kind(model) != JunkKind.None)
            {
                if (!FmField.Marked.Remove(model) && FmField.Marked.Count < 5)
                {
                    FmField.Marked.Add(model);
                    FmSfx.Play(FmSfx.Mark);
                }
                FmField.NotifyChanged();
                UpdateMarks();
                return true;
            }
        }
        if (ZoneUnderMouse(mouse) is { } z) { FmField.TogglePosition(z); return true; }
        return false;
    }

    static readonly System.Reflection.PropertyInfo? HoveredNodeProp = AccessTools.Property(typeof(NTargetManager), "HoveredNode");
    static readonly System.Reflection.MethodInfo? FinishTargetingM = AccessTools.Method(typeof(NTargetManager), "FinishTargeting", new[] { typeof(bool) });
    static readonly System.Reflection.MethodInfo? OnCreatureHoveredM = AccessTools.Method(typeof(NTargetManager), "OnCreatureHovered");

    /// <summary>The boss monster whose card is under the mouse, if your monster may attack it.</summary>
    static NCreature? RivalTargetUnderMouse(Vector2 mouse)
    {
        if (!FmRival.Active || NCombatRoom.Instance is not { } room) return null;
        foreach (var s in FmRival.Slots.Reverse())
            if (s.RivalCreature is { IsAlive: true } c && FmRival.CanBeAttacked(c) && s.Visual.IsMouseOverCard(mouse) && room.GetCreatureNode(c) is { } n)
                return n;
        return null;
    }

    static void PickTarget(NTargetManager tm, NCreature node)
    {
        try
        {
            HoveredNodeProp?.SetValue(tm, node);
            FinishTargetingM?.Invoke(tm, new object[] { false });
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] pick target: " + ex.Message); }
    }

    /// <summary>While aiming, hovering a boss monster's card lights it up as the target (reticle and red arrow).</summary>
    static void SyncRivalHover()
    {
        if (!FmField.Aiming || NTargetManager.Instance is not { IsInSelection: true } tm || Engine.GetMainLoop() is not SceneTree t) return;
        if (RivalTargetUnderMouse(t.Root.GetMousePosition()) is not { } n) return;
        if (HoveredNodeProp?.GetValue(tm) == n) return;
        try { OnCreatureHoveredM?.Invoke(tm, new object[] { n }); } catch (Exception ex) { Log.Warn("[FMDuelist] rival hover: " + ex.Message); }
    }

    static FmSlot? ZoneUnderMouse(Vector2 mouse)
    {
        var player = FmField.Duelist;
        return FmField.Slots.LastOrDefault(z => z.Alive(player) && z.Visual.IsMouseOver(mouse));
    }

    static bool HandleLeftClick(Vector2 mouse)
    {
        var player = FmField.Duelist;
        if (player == null || ZoneUnderMouse(mouse) is not { } zone) return false;
        if (zone.Position != FmPosition.Attack || zone.AttackedThisTurn) return false;
        if (FmRival.BlockFirstTurnAttack()) return true;
        _ = AimAndAttack(player, zone);
        return true;
    }

    /// <summary>The game's own targeting arrow from the monster card; the chosen enemy gets that monster's attack.</summary>
    static async Task AimAndAttack(MegaCrit.Sts2.Core.Entities.Players.Player player, FmSlot zone)
    {
        try
        {
            var tm = NTargetManager.Instance;
            FmFieldTarget.MonsterArrow = false; FmFieldTarget.EquipArrow = null;
            tm.StartTargeting(MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyEnemy, zone.Visual.CardCenter(), TargetMode.ClickMouseToTarget,
                () => !zone.Alive(player), n => n is NCreature c && c.Entity.IsEnemy && c.Entity.IsAlive && (!FmRival.Active || FmRival.CanBeAttacked(c.Entity)));
            FmField.Aiming = true;
            FmField.Attacker = zone;
            FmField.NotifyChanged();
            var node = await tm.SelectionFinished();
            FmField.Aiming = false;
            FmField.Attacker = null;
            FmField.NotifyChanged();
            if (node is NCreature target && target.Entity.IsEnemy && target.Entity.IsAlive)
            {
                // a Guardian Star exchange: show it and ask first
                if (FmRival.Active && FmRival.SlotOf(target.Entity) is { MonsterId: > 0 } def && !await FmStarWarning.Confirm(zone, def, player)) return;
                FmAttackAction.Request(player, zone.Index, target.Entity);
            }
        }
        catch (Exception ex) { FmField.Aiming = false; Log.Warn("[FMDuelist] aim: " + ex.Message); }
    }

    static void Process()
    {
        try { UpdateMarks(); SyncRivalHover(); foreach (var z in FmField.Slots) z.Visual.Tick(); foreach (var z in FmRival.Slots) z.Visual.Tick(); if (Engine.GetMainLoop() is SceneTree t) FmZone.Tick(t.Root.GetMousePosition()); }
        catch (Exception ex) { Log.Warn("[FMDuelist] frame: " + ex.Message); }
    }

    /// <summary>Marked cards: raised, yellow glow border, order number in the top-right corner.</summary>
    static void UpdateMarks()
    {
        var hand = NPlayerHand.Instance;
        foreach (var kv in _marks.ToList())
        {
            if (FmField.Marked.Contains(kv.Key) && GodotObject.IsInstanceValid(kv.Value)) continue;
            if (GodotObject.IsInstanceValid(kv.Value))
            {
                if (kv.Value.GetParent()?.GetParent() is NCard oldCard) oldCard.Position = oldCard.Position with { Y = 0 };
                kv.Value.QueueFree();
            }
            _marks.Remove(kv.Key);
        }
        // while a choice screen is up (picking the zone, the position...) or a marked card has left the hand, its marker hides
        bool overlay0 = FmZone.OverlayOpen;
        foreach (var kv in _marks)
            if (GodotObject.IsInstanceValid(kv.Value))
                kv.Value.Visible = !overlay0 && hand != null && hand.GetCardHolder(kv.Key)?.CardNode != null;
        if (hand == null || overlay0) return;
        for (int i = 0; i < FmField.Marked.Count; i++)
        {
            var m = FmField.Marked[i];
            if (hand.GetCardHolder(m)?.CardNode is not NCard card) continue;
            var body = card.Body ?? (Control)card;
            if (!_marks.TryGetValue(m, out var overlay) || !GodotObject.IsInstanceValid(overlay))
            {
                overlay = MakeOverlay(body.Size == Vector2.Zero ? NCard.defaultSize : body.Size);
                body.AddChild(overlay);
                _marks[m] = overlay;
            }
            if (overlay.GetNode<Label>("Num") is { } num) num.Text = (i + 1).ToString();
            card.Position = card.Position with { Y = -70 };
        }
    }

    static Control MakeOverlay(Vector2 size)
    {
        var root = new Control { Size = size, Position = -size / 2, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 5 };
        var glow = new Panel { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        glow.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            DrawCenter = true, BgColor = new Color(1f, 0.9f, 0.2f, 0.12f), BorderColor = new Color(1f, 0.88f, 0.15f),
            BorderWidthLeft = 10, BorderWidthRight = 10, BorderWidthTop = 10, BorderWidthBottom = 10,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ShadowColor = new Color(1f, 0.85f, 0.1f, 0.75f), ShadowSize = 22,
        });
        root.AddChild(glow);
        var num = new Label
        {
            Name = "Num", Position = new Vector2(size.X - 92, 4), Size = new Vector2(84, 84),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        num.AddThemeFontSizeOverride("font_size", 72);
        num.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.3f));
        num.AddThemeColorOverride("font_outline_color", new Color(0.3f, 0.05f, 0f));
        num.AddThemeConstantOverride("outline_size", 18);
        root.AddChild(num);
        return root;
    }
}

/// <summary>
/// The summoned monster stands in Osty's place as the game's own card node: upright in Attack, sideways in Defense.
/// It flies into the target when it attacks and burns away when destroyed.
/// </summary>
public sealed class FmSlotVisual
{
    readonly FmSlot _slot;
    public FmSlotVisual(FmSlot slot) { _slot = slot; }

    Node2D? _root;
    Node2D? _group;
    Node2D? _holder;
    NCard? _card;
    Label? _stats, _pos;
    ShaderMaterial? _burn;
    int _shownId;
    bool _subscribed;
    const float CardScale = 0.55f;
    /// <summary>How big zone cards are drawn (for the zone picker's outlines).</summary>
    public static float CardScaleNow => CardScale;
    static readonly Vector2 Home = new(0, -150);

    const string BurnShader = @"shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform float progress = 0.0;
float h(vec2 p){ return fract(sin(dot(p, vec2(12.9898,78.233))) * 43758.5453); }
float n(vec2 p){ vec2 i=floor(p); vec2 f=fract(p); f=f*f*(3.0-2.0*f);
  return mix(mix(h(i),h(i+vec2(1,0)),f.x), mix(h(i+vec2(0,1)),h(i+vec2(1,1)),f.x), f.y); }
void fragment(){
  vec4 c = textureLod(screen_texture, SCREEN_UV, 0.0);
  if (c.a > 0.0001) c.rgb /= c.a;
  vec2 q = SCREEN_UV * vec2(64.0, 36.0);
  float v = n(q)*0.6 + n(q*2.7)*0.4;
  v = v*0.85 + (1.0 - fract(SCREEN_UV.y*3.0))*0.15;
  if (progress > 0.0 && v < progress) discard;
  float edge = progress > 0.0 ? smoothstep(progress, progress + 0.09, v) : 1.0;
  vec3 fire = mix(vec3(1.0,0.95,0.4), vec3(1.0,0.35,0.05), clamp((v-progress)*12.0,0.0,1.0));
  COLOR = vec4(mix(fire*1.6, c.rgb, edge), c.a);
}";

    bool Valid => _root != null && GodotObject.IsInstanceValid(_root);

    public void Attach(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (!_subscribed) { FmField.Changed += Refresh; _subscribed = true; }
        var creature = _slot.Creature(player);
        var node = creature != null ? NCombatRoom.Instance?.GetCreatureNode(creature) : null;
        if (node == null) return;
        if (Valid && _root!.GetParent() == node.Visuals) { Refresh(); return; }
        _root = new Node2D { Name = "FMDuelistCard" + _slot.Index };
        _burn = new ShaderMaterial { Shader = new Shader { Code = BurnShader } };
        _group = new Node2D();
        _holder = new Node2D { Position = Home, Scale = Vector2.One * CardScale };
        _group.AddChild(_holder);
        _root.AddChild(_group);
        _stats = MakeLabel(28, new Vector2(-120, -330));
        _pos = MakeLabel(20, new Vector2(-120, -296));
        node.Visuals.AddChild(_root);
        _shownId = 0;
        Refresh();
        FmDuelLayout.Place(player, _slot);
    }

    Label MakeLabel(int size, Vector2 pos)
    {
        var l = new Label { Position = pos, Size = new Vector2(240, 36), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 8);
        _root!.AddChild(l);
        return l;
    }

    /// <summary>Where this zone's card rests (world coordinates), once it's showing.</summary>
    public Vector2? RestingSpot() => Valid && _group != null ? _group.GetGlobalTransform() * Home : null;

    public Vector2 CardCenter() => Valid && _holder != null ? _holder.GetGlobalTransformWithCanvas().Origin : Vector2.Zero;

    /// <summary>Is the mouse on this zone's card itself (not counting the creature's hit area)?</summary>
    public bool IsMouseOverCard(Vector2 screenMouse)
    {
        if (!Valid || _card == null || !GodotObject.IsInstanceValid(_card) || !_group!.Visible || _slot.MonsterId == 0 || FmZone.OverlayOpen) return false;
        var local = _holder!.GetGlobalTransformWithCanvas().AffineInverse() * screenMouse;
        var size = _slot.Position == FmPosition.Defense ? new Vector2(NCard.defaultSize.Y, NCard.defaultSize.X) : NCard.defaultSize;
        return new Rect2(-NCard.defaultSize / 2, NCard.defaultSize).HasPoint(local) || new Rect2(-size / 2, size).HasPoint(local);
    }

    public bool IsMouseOver(Vector2 screenMouse)
    {
        if (!Valid || _card == null || !GodotObject.IsInstanceValid(_card) || !_group!.Visible || _slot.MonsterId == 0 || FmZone.OverlayOpen) return false;
        var local = _holder!.GetGlobalTransformWithCanvas().AffineInverse() * screenMouse;
        var half = NCard.defaultSize / 2;
        if (new Rect2(-half, NCard.defaultSize).HasPoint(local)) return true;
        var creature = FmField.Duelist is { } pl ? _slot.Creature(pl) : null;
        return creature != null && NCombatRoom.Instance?.GetCreatureNode(creature) is { } n && n.Hitbox.GetGlobalRect().HasPoint(screenMouse);
    }

    public void Tick()
    {
        if (Valid && _root!.GetParent() is NCreatureVisuals vis && vis.GetCurrentBody() is { } body && body.Visible) body.Visible = false;
        FitHitbox();
    }

    /// <summary>
    /// The creature's hit area follows the card, so arrows (fusing onto your monster, attacking a boss's monster)
    /// land when you drop them on the card itself.
    /// </summary>
    void FitHitbox()
    {
        try
        {
            if (!Valid || _holder == null || _card == null || !GodotObject.IsInstanceValid(_card) || _slot.MonsterId == 0) return;
            if (_root!.GetParent() is not NCreatureVisuals vis || vis.GetParent() is not NCreature creature || creature.Hitbox is not { } hit) return;
            // while a card choice or other screen is open, the monster card mustn't catch the clicks meant for it
            hit.MouseFilter = FmZone.OverlayOpen ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
            if (hit.GetParent() is not CanvasItem parent) return;
            var cardXf = _holder.GetGlobalTransformWithCanvas();
            var size = NCard.defaultSize * cardXf.Scale.Abs();
            if (_slot.Position == FmPosition.Defense) size = new Vector2(size.Y, size.X);
            var parentXf = parent.GetGlobalTransformWithCanvas();
            var center = parentXf.AffineInverse() * cardXf.Origin;
            var localSize = size / parentXf.Scale.Abs();
            hit.Position = center - localSize / 2;
            hit.Size = localSize;
        }
        catch { }
    }

    Sprite2D? _backSprite;
    bool _wasFaceDown;

    /// <summary>Face down: the FM card back covers the card. Turning face up flips it over.</summary>
    void SetFaceDown(bool down)
    {
        if (_holder == null) return;
        if (_backSprite == null || !GodotObject.IsInstanceValid(_backSprite))
        {
            var tex = FmRivalHand.Back();
            _backSprite = new Sprite2D { Texture = tex, Scale = NCard.defaultSize / new Vector2(tex.GetWidth(), tex.GetHeight()), ZIndex = 10, Visible = false };
            _holder.AddChild(_backSprite);
        }
        _holder.MoveChild(_backSprite, _holder.GetChildCount() - 1);
        if (down) { _backSprite.Visible = true; _wasFaceDown = true; return; }
        if (!_wasFaceDown) { _backSprite.Visible = false; return; }
        // flip face up
        _wasFaceDown = false;
        var holder = _holder; var back = _backSprite;
        var t = holder.CreateTween();
        t.TweenProperty(holder, "scale:x", 0.01f, 0.12);
        t.TweenCallback(Callable.From(() => back.Visible = false));
        t.TweenProperty(holder, "scale:x", CardScale, 0.12);
        FmSfx.Play(FmSfx.Flip);
    }

    NCard? MakeCard(int id)
    {
        var player = FmField.Duelist;
        var combat = player?.Creature.CombatState;
        if (player == null || combat == null) return null;
        var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(Cards.FmSheet.TypeFor(id)));
        var model = combat.CreateCard(canonical, player);
        var card = NCard.Create(model);
        if (card == null) return null;
        card.Position = Vector2.Zero;
        card.Scale = Vector2.One;
        card.Rotation = 0;
        card.MouseFilter = Control.MouseFilterEnum.Ignore;
        return card;
    }

    void Refresh()
    {
        if (!Valid || _holder == null || _stats == null || _pos == null || _burn == null || _group == null) return;
        int id = _slot.MonsterId;
        if (id == 0)
        {
            if (_shownId != 0) BurnAway();
            _stats.Visible = _pos.Visible = false;
            return;
        }
        _group.Visible = true;
        _group.Modulate = Colors.White;
        _holder.Scale = Vector2.One * CardScale;
        if (_shownId != id || _card == null || !GodotObject.IsInstanceValid(_card))
        {
            if (_card != null && GodotObject.IsInstanceValid(_card)) { _card.GetParent()?.RemoveChild(_card); _card.QueueFree(); }
            _card = MakeCard(id);
            if (_card != null)
            {
                _holder.AddChild(_card);
                _card.UpdateVisuals(MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand, MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode.None);
            }
            bool fresh = _shownId != id;
            _shownId = id;
            _holder.Position = Home;
            if (fresh && _card != null) SummonFx(_card);
        }
        var tween = _root!.CreateTween();
        tween.TweenProperty(_holder, "rotation", _slot.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.2);
        SetFaceDown(_slot.FaceDown);
        _stats.Visible = _pos.Visible = true;
        _stats.Text = _slot.FaceDown && _slot.Rival ? "? / ?" : _slot.FaceDown ? $"{_slot.Atk} / {_slot.Def}  (set)" : $"{_slot.Atk} / {_slot.Def}";
        _stats.AddThemeColorOverride("font_color", _slot.Position == FmPosition.Attack ? new Color(1f, 0.85f, 0.4f) : new Color(0.6f, 0.85f, 1f));
        _pos.Text = _slot.Rival ? (_slot.Position == FmPosition.Defense ? "DEF" : "ATK") : FmField.Aiming && !_slot.AttackedThisTurn ? "Choose an enemy"
            : _slot.Position == FmPosition.Defense ? "DEF  (right-click: ATK)"
            : _slot.AttackedThisTurn ? "ATK  (attacked - locked)"
            : FmRival.FirstTurnNoAttack ? "ATK  (no attacks on turn 1)" : "ATK  click: attack / right-click: DEF";
        if (_slot.Star > 0 && !(_slot.Rival && _slot.FaceDown)) _pos.Text = $"{FmStars.Name(_slot.Star)}   " + _pos.Text;
    }

    /// <summary>
    /// Summon: the card drops onto its zone spinning and growing, and lands with a golden light pillar, an expanding
    /// ring and rising sparkles.
    /// </summary>
    void SummonFx(Control card)
    {
        if (_group == null || _root == null) return;
        var baseScale = card.Scale;
        var basePos = card.Position;
        card.PivotOffset = card.Size / 2;
        card.Scale = baseScale * 0.15f;
        card.Position = basePos + new Vector2(0, -520);
        card.Rotation = -Mathf.Tau;
        card.Modulate = new Color(1.6f, 1.5f, 1.1f, 0f);
        var drop = card.CreateTween().SetParallel();
        drop.TweenProperty(card, "position", basePos, 0.38).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        drop.TweenProperty(card, "scale", baseScale, 0.38).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        drop.TweenProperty(card, "rotation", 0f, 0.38).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        drop.TweenProperty(card, "modulate:a", 1f, 0.15);
        drop.Chain().TweenProperty(card, "modulate", Colors.White, 0.35);
        drop.Chain().TweenCallback(Callable.From(() => Landing()));
        FmSfx.SummonSound();
    }

    void Landing()
    {
        if (_group == null || !GodotObject.IsInstanceValid(_group)) return;
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        // light pillar
        var grad = new Gradient();
        grad.SetColor(0, new Color(1f, 0.85f, 0.35f, 0f));
        grad.SetColor(1, new Color(1f, 0.85f, 0.35f, 0.9f));
        var pillarTex = new GradientTexture2D { Gradient = grad, Width = 8, Height = 256, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1) };
        var pillar = new Sprite2D { Texture = pillarTex, Material = add, Position = Home + new Vector2(0, -260), Scale = new Vector2(24, 2.6f) };
        _group.AddChild(pillar);
        var pt = pillar.CreateTween().SetParallel();
        pt.TweenProperty(pillar, "scale:x", 2f, 0.55).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        pt.TweenProperty(pillar, "modulate:a", 0f, 0.55);
        pt.Chain().TweenCallback(Callable.From(pillar.QueueFree));
        // expanding ring on the floor
        var ring = new Line2D { Width = 6, DefaultColor = new Color(1f, 0.9f, 0.5f, 0.9f), Material = add, Position = Home + new Vector2(0, 110), Closed = true };
        for (int i = 0; i < 48; i++) { float a = Mathf.Tau * i / 48; ring.AddPoint(new Vector2(Mathf.Cos(a) * 60, Mathf.Sin(a) * 18)); }
        _group.AddChild(ring);
        var rt = ring.CreateTween().SetParallel();
        rt.TweenProperty(ring, "scale", Vector2.One * 3.2f, 0.5).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        rt.TweenProperty(ring, "modulate:a", 0f, 0.5);
        rt.Chain().TweenCallback(Callable.From(ring.QueueFree));
        // sparkles rising
        var sparks = new CpuParticles2D
        {
            Position = Home + new Vector2(0, 90), Amount = 60, Lifetime = 1.0, OneShot = true, Explosiveness = 0.8f, Material = add,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(90, 20),
            Direction = new Vector2(0, -1), Spread = 25, InitialVelocityMin = 120, InitialVelocityMax = 320, Gravity = new Vector2(0, 80),
            ScaleAmountMin = 2, ScaleAmountMax = 5, Color = new Color(1f, 0.85f, 0.4f),
        };
        _group.AddChild(sparks);
        sparks.Emitting = true;
        var st = sparks.CreateTween();
        st.TweenInterval(1.4);
        st.TweenCallback(Callable.From(sparks.QueueFree));
    }

    /// <summary>The card flies into the target; returns when it connects. Call FlyHome afterwards.</summary>
    public async Task FlyTo(Vector2 targetScreen)
    {
        if (!Valid || _holder == null) return;
        var parentXf = _group!.GetGlobalTransformWithCanvas();
        var local = parentXf.AffineInverse() * targetScreen;
        var t = _holder.CreateTween();
        t.TweenProperty(_holder, "position", Home + (local - Home) * 0.25f, 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.Parallel().TweenProperty(_holder, "rotation", 0.25f, 0.12);
        t.TweenProperty(_holder, "position", local, 0.14).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        await t.ToSignal(t, Tween.SignalName.Finished);
    }

    /// <summary>
    /// FM-style equip: the equip card appears above the monster, spins down into it, the two merge in a flash, a shine
    /// sweeps across the monster's card and "+X ATK/DEF" rises from it.
    /// </summary>
    public async Task EquipFx(int equipId, int value)
    {
        if (!Valid || _holder == null || _group == null || _root == null) return;
        FmSfx.EquipSound();
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        var eq = new Node2D { Position = Home + new Vector2(0, -330), Scale = Vector2.One * 0.75f, Modulate = new Color(1, 1, 1, 0) };
        eq.AddChild(FmCardFace.Create(equipId));
        _group.AddChild(eq);
        var t = eq.CreateTween();
        t.TweenProperty(eq, "modulate:a", 1f, 0.15);
        t.TweenInterval(0.25);
        t.TweenProperty(eq, "position", Home, 0.32).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        t.Parallel().TweenProperty(eq, "scale", Vector2.One * 0.25f, 0.32);
        t.Parallel().TweenProperty(eq, "rotation", Mathf.Tau, 0.32);
        await t.ToSignal(t, Tween.SignalName.Finished);
        eq.QueueFree();
        // merge flash
        var glow = new Polygon2D { Polygon = new[] { new Vector2(-95, -135), new Vector2(95, -135), new Vector2(95, 135), new Vector2(-95, 135) }, Color = new Color(1f, 0.95f, 0.7f, 0.9f), Material = add, Position = Home };
        _group.AddChild(glow);
        var g = glow.CreateTween();
        g.TweenProperty(glow, "modulate:a", 0f, 0.35);
        g.TweenCallback(Callable.From(glow.QueueFree));
        var pulse = _holder.CreateTween();
        pulse.TweenProperty(_holder, "modulate", new Color(1.9f, 1.8f, 1.3f), 0.1);
        pulse.TweenProperty(_holder, "modulate", Colors.White, 0.45);
        // shine sweep across the card
        var shine = new Polygon2D { Polygon = new[] { new Vector2(-14, -150), new Vector2(14, -150), new Vector2(14, 150), new Vector2(-14, 150) }, Color = new Color(1, 1, 1, 0.85f), Material = add, Position = Home + new Vector2(-120, 0), Rotation = 0.45f };
        _group.AddChild(shine);
        var sw = shine.CreateTween();
        sw.TweenProperty(shine, "position", Home + new Vector2(120, 0), 0.4).SetTrans(Tween.TransitionType.Sine);
        sw.Parallel().TweenProperty(shine, "modulate:a", 0f, 0.4).SetDelay(0.15);
        sw.TweenCallback(Callable.From(shine.QueueFree));
        var sparks = new CpuParticles2D
        {
            Position = Home, Amount = 40, Lifetime = 0.8, OneShot = true, Explosiveness = 0.9f, Material = add,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(80, 115),
            Direction = Vector2.Up, Spread = 60, InitialVelocityMin = 40, InitialVelocityMax = 140, Gravity = new Vector2(0, -40),
            ScaleAmountMin = 2, ScaleAmountMax = 5, Color = new Color(1f, 0.9f, 0.5f),
        };
        _group.AddChild(sparks);
        sparks.Emitting = true;
        var st = sparks.CreateTween(); st.TweenInterval(1.2); st.TweenCallback(Callable.From(sparks.QueueFree));
        var label = new Label { Text = $"+{value} ATK/DEF", Position = Home + new Vector2(-120, -175), Size = new Vector2(240, 40), HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 30);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _group.AddChild(label);
        var f = label.CreateTween().SetParallel();
        f.TweenProperty(label, "position:y", label.Position.Y - 70, 1.1);
        f.TweenProperty(label, "modulate:a", 0f, 0.9).SetDelay(0.4);
        f.Chain().TweenCallback(Callable.From(label.QueueFree));
        await sw.ToSignal(sw, Tween.SignalName.Finished);
    }

    /// <summary>Guardian Star boost: the card glows gold (ATK) or blue (DEF) and "+500" floats up.</summary>
    public void StarFlash(bool atk)
    {
        if (!Valid || _holder == null || _group == null || _root == null) return;
        var col = atk ? new Color(1f, 0.8f, 0.3f) : new Color(0.5f, 0.75f, 1f);
        var t = _holder.CreateTween();
        t.TweenProperty(_holder, "modulate", new Color(col.R * 1.8f, col.G * 1.8f, col.B * 1.8f), 0.15);
        t.TweenProperty(_holder, "modulate", Colors.White, 0.4);
        var label = new Label { Text = atk ? "+500 ATK" : "+500 DEF", Position = Home + new Vector2(-90, -170), Size = new Vector2(180, 40), HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 30);
        label.AddThemeColorOverride("font_color", col);
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _group.AddChild(label);
        var f = label.CreateTween().SetParallel();
        f.TweenProperty(label, "position:y", label.Position.Y - 70, 1.0);
        f.TweenProperty(label, "modulate:a", 0f, 1.0).SetDelay(0.3);
        f.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    public void FlyHome()
    {
        if (!Valid || _holder == null) return;
        var t = _holder.CreateTween();
        t.TweenProperty(_holder, "position", Home, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.Parallel().TweenProperty(_holder, "rotation", _slot.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.3);
    }

    void BurnAway()
    {
        _shownId = 0;
        var group = _group!; var holder = _holder!;
        // FM-style burn: the card flares orange, chars to black and crumbles away in sparks
        var sparks = new CpuParticles2D
        {
            Position = holder.Position, Amount = 90, Lifetime = 0.9, OneShot = true, Explosiveness = 0.6f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(80, 115),
            Direction = new Vector2(0, -1), Spread = 35, InitialVelocityMin = 60, InitialVelocityMax = 180, Gravity = new Vector2(0, -60),
            ScaleAmountMin = 3, ScaleAmountMax = 7, Color = new Color(1f, 0.55f, 0.1f),
        };
        _root!.AddChild(sparks);
        sparks.Emitting = true;
        var t = _root.CreateTween();
        t.TweenProperty(group, "modulate", new Color(2.2f, 1.2f, 0.35f, 1f), 0.15);
        t.TweenProperty(group, "modulate", new Color(0.25f, 0.08f, 0.02f, 1f), 0.45);
        t.Parallel().TweenProperty(holder, "scale", Vector2.One * CardScale * 0.92f, 0.45);
        t.TweenProperty(group, "modulate:a", 0f, 0.35);
        t.TweenCallback(Callable.From(() => { if (_shownId == 0) group.Visible = false; }));
        t.TweenInterval(1.0);
        t.TweenCallback(Callable.From(sparks.QueueFree));
    }
}
