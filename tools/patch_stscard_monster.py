import os, re
H = os.path.expanduser('~/fmsts')
p = f'{H}/src/FMDuelist/Game/FmUi.cs'
s = open(p, encoding='utf-8').read()

# 1) input: let the game's targeting manager have clicks while it is aiming; left-click on monster starts the game's arrow
s = s.replace('''        if (e is not InputEventMouseButton { Pressed: true } mb) return false;''',
'''        if (e is not InputEventMouseButton { Pressed: true } mb) return false;
        if (NTargetManager.Instance is { IsInSelection: true }) return false;''', 1)

old_left = s[s.index('    static bool HandleLeftClick(Vector2 mouse)'):s.index('    static void Process()')]
new_left = '''    static bool HandleLeftClick(Vector2 mouse)
    {
        var player = FmField.Duelist;
        if (player == null || !FmField.HasMonster(player)) return false;
        if (!FmOstyVisual.IsMouseOver(mouse) || FmField.Position != FmPosition.Attack || FmField.AttackedThisTurn) return false;
        _ = AimAndAttack(player);
        return true;
    }

    /// <summary>The game's own targeting arrow from the monster card; the chosen enemy gets the attack.</summary>
    static async Task AimAndAttack(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            var tm = NTargetManager.Instance;
            tm.StartTargeting(MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyEnemy, FmOstyVisual.CardCenter(), TargetMode.ClickMouseToTarget,
                () => !FmField.HasMonster(player), n => n is NCreature c && c.Entity.IsEnemy && c.Entity.IsAlive);
            FmField.Aiming = true;
            FmField.NotifyChanged();
            var node = await tm.SelectionFinished();
            FmField.Aiming = false;
            FmField.NotifyChanged();
            if (node is NCreature target && target.Entity.IsEnemy && target.Entity.IsAlive)
                FmAttackAction.Request(player, target.Entity);
        }
        catch (Exception ex) { FmField.Aiming = false; Log.Warn("[FMDuelist] aim: " + ex.Message); }
    }

'''
s = s.replace(old_left, new_left, 1)
s = s.replace("        if (FmField.Aiming) { FmField.Aiming = false; FmField.NotifyChanged(); return true; }\n", "", 1)

# 2) field visual: the real StS card node (NCard) inside a CanvasGroup (for the burn), flies to the target on attack
start = s.index('/// <summary>Shows the summoned FM card in Osty')
s = s[:start] + '''/// <summary>
/// The summoned monster stands in Osty's place as the game's own card node: upright in Attack, sideways in Defense.
/// It flies into the target when it attacks and burns away when destroyed.
/// </summary>
public static class FmOstyVisual
{
    static Node2D? _root;
    static CanvasGroup? _group;
    static Node2D? _holder;
    static NCard? _card;
    static Label? _stats, _pos;
    static ShaderMaterial? _burn;
    static int _shownId;
    static bool _subscribed;
    const float CardScale = 0.55f;
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

    static bool Valid => _root != null && GodotObject.IsInstanceValid(_root);

    public static void Attach(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (!_subscribed) { FmField.Changed += Refresh; _subscribed = true; }
        var node = player.Osty != null ? NCombatRoom.Instance?.GetCreatureNode(player.Osty) : null;
        if (node == null) return;
        if (Valid && _root!.GetParent() == node.Visuals) { Refresh(); return; }
        _root = new Node2D { Name = "FMDuelistCard" };
        _burn = new ShaderMaterial { Shader = new Shader { Code = BurnShader } };
        _group = new CanvasGroup { Material = _burn };
        _holder = new Node2D { Position = Home, Scale = Vector2.One * CardScale };
        _group.AddChild(_holder);
        _root.AddChild(_group);
        _stats = MakeLabel(28, new Vector2(-120, -330));
        _pos = MakeLabel(20, new Vector2(-120, -296));
        node.Visuals.AddChild(_root);
        _shownId = 0;
        Refresh();
    }

    static Label MakeLabel(int size, Vector2 pos)
    {
        var l = new Label { Position = pos, Size = new Vector2(240, 36), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 8);
        _root!.AddChild(l);
        return l;
    }

    public static Vector2 CardCenter() => Valid && _holder != null ? _holder.GetGlobalTransformWithCanvas().Origin : Vector2.Zero;

    public static bool IsMouseOver(Vector2 screenMouse)
    {
        if (!Valid || _card == null || !GodotObject.IsInstanceValid(_card) || !_group!.Visible || FmField.MonsterId == 0) return false;
        var local = _holder!.GetGlobalTransformWithCanvas().AffineInverse() * screenMouse;
        var half = NCard.defaultSize / 2;
        if (new Rect2(-half, NCard.defaultSize).HasPoint(local)) return true;
        var osty = FmField.Duelist?.Osty;
        return osty != null && NCombatRoom.Instance?.GetCreatureNode(osty) is { } n && n.Hitbox.GetGlobalRect().HasPoint(screenMouse);
    }

    public static void Tick()
    {
        if (Valid && _root!.GetParent() is NCreatureVisuals vis && vis.GetCurrentBody() is { } body && body.Visible) body.Visible = false;
    }

    static NCard? MakeCard(int id)
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

    static void Refresh()
    {
        if (!Valid || _holder == null || _stats == null || _pos == null || _burn == null || _group == null) return;
        int id = FmField.MonsterId;
        if (id == 0)
        {
            if (_shownId != 0) BurnAway();
            _stats.Visible = _pos.Visible = false;
            return;
        }
        _burn.SetShaderParameter("progress", 0.0f);
        _group.Visible = true;
        if (_shownId != id || _card == null || !GodotObject.IsInstanceValid(_card))
        {
            if (_card != null && GodotObject.IsInstanceValid(_card)) { _card.GetParent()?.RemoveChild(_card); _card.QueueFree(); }
            _card = MakeCard(id);
            if (_card != null)
            {
                _holder.AddChild(_card);
                _card.UpdateVisuals(MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand, MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode.None);
            }
            _shownId = id;
            _holder.Position = Home;
        }
        var tween = _root!.CreateTween();
        tween.TweenProperty(_holder, "rotation", FmField.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.2);
        _stats.Visible = _pos.Visible = true;
        _stats.Text = $"{FmField.Atk} / {FmField.Def}";
        _stats.AddThemeColorOverride("font_color", FmField.Position == FmPosition.Attack ? new Color(1f, 0.85f, 0.4f) : new Color(0.6f, 0.85f, 1f));
        _pos.Text = FmField.Aiming ? "Choose an enemy"
            : FmField.Position == FmPosition.Defense ? "DEF  (right-click: ATK)"
            : FmField.AttackedThisTurn ? "ATK  (attacked)" : "ATK  click: attack / right-click: DEF";
    }

    /// <summary>The card flies into the target; returns when it connects. Call FlyHome afterwards.</summary>
    public static async Task FlyTo(Vector2 targetScreen)
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

    public static void FlyHome()
    {
        if (!Valid || _holder == null) return;
        var t = _holder.CreateTween();
        t.TweenProperty(_holder, "position", Home, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.Parallel().TweenProperty(_holder, "rotation", FmField.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.3);
    }

    static void BurnAway()
    {
        _shownId = 0;
        var burn = _burn!; var group = _group!;
        var t = _root!.CreateTween();
        t.TweenMethod(Callable.From<float>(p => burn.SetShaderParameter("progress", p)), 0.001f, 1.05f, 1.1);
        t.TweenCallback(Callable.From(() => { if (_shownId == 0) group.Visible = false; }));
    }
}
'''
open(p, 'w', encoding='utf-8').write(s)

# 3) attack: fly the card into the enemy, hit, fly back
p = f'{H}/src/FMDuelist/Game/FmField.cs'
s = open(p, encoding='utf-8').read()
a = '''        await DamageCmd.Attack(Atk).FromOsty(osty, SourceCard).Targeting(target)
            .WithHitFx("vfx/vfx_attack_slash", null, "blunt_attack.mp3").Execute(ctx);'''
b = '''        if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(target) is { } tn)
            await FmOstyVisual.FlyTo(tn.Hitbox.GetGlobalRect().GetCenter());
        await DamageCmd.Attack(Atk).FromOsty(osty, SourceCard).Targeting(target).WithNoAttackerAnim()
            .WithHitFx("vfx/vfx_attack_slash", null, "blunt_attack.mp3").Execute(ctx);
        FmOstyVisual.FlyHome();'''
assert a in s
s = s.replace(a, b, 1)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
