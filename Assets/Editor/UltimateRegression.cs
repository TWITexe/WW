using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Opt-in host integration: real spawn/RPC/damage/motor code in Play Mode on an isolated test floor.
[InitializeOnLoad]
public static class UltimateRegression
{
    const string Flag = "Wizard.Ultimates.Regression";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int stage, assertions;
    static double next, started;
    static PlayerUltimate owner;
    static NetManager manager;
    static UltimateCatalog catalog;
    static readonly List<string> results = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly List<GameObject> temporary = new List<GameObject>();
    static readonly Vector3 Origin = new Vector3(0, 200, 0);

    static UltimateRegression()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Flag + ".Restore", false)) return;
            SessionState.SetBool(Flag + ".Restore", false);
            string path = SessionState.GetString(Flag + ".Scene", "Assets/Scenes/SampleScene.unity");
            if (!string.IsNullOrEmpty(path)) EditorSceneManager.OpenScene(path);
        };
    }
    [MenuItem("Tools/Wizard War/Validate ultimates in Play Mode")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before the regression.");
        ValidateAssets();
        SessionState.SetString(Flag + ".Scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        SessionState.SetBool(Flag, true); stage = 0; started = 0; assertions = 0;
        results.Clear(); errors.Clear(); temporary.Clear();
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
        EditorApplication.isPlaying = true;
    }
    public static void ValidateAssets()
    {
        catalog = AssetDatabase.LoadAssetAtPath<UltimateCatalog>("Assets/Spells/Ultimates/UltimateCatalog.asset");
        Check(catalog != null && catalog.definitions.Length == 10, "ten definitions");
        Check(catalog.definitions.Select(d => UltimateCatalog.Mask(d.elements)).Distinct().Count() == 10, "ten unique elemental sets");
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        Check(player.GetComponentInChildren<PlayerUltimate>(true).presentation != null, "saved player presentation");
        var view=player.GetComponentInChildren<UltimatePresentation>(true);
        Check(view.crystals.Length==4 && view.polarGuide!=null,"four crystals and saved polar guide");
        Check(view.meteorFill.fillMethod==UnityEngine.UI.Image.FillMethod.Horizontal && view.meteorFill.color.r>.8f && view.meteorFill.color.g<.1f,"red rectangular meteor bar");
        var flightAnimation=view.spiritModel!=null?view.spiritModel.GetComponentInChildren<Animation>(true):null;
        Check(flightAnimation!=null && new[]{"Hover","Walk","SwingLeft","SwingRight"}.All(n=>flightAnimation.GetClip(n)!=null),"saved idle, walk and directional golem attacks");
        Check(catalog.Get(UltimateKind.MirrorLabyrinth).duration==27 && catalog.Get(UltimateKind.EarthDepths).duration==27 && catalog.Get(UltimateKind.GlacierRam).duration==11,"extended ultimate durations");
        Check(view.smoothRoots.Contains(view.spiritModel) && view.smoothRoots.Contains(view.phoenixAura.transform),"form and aura share movement smoothing");
        foreach (var definition in catalog.definitions)
        {
            var e = definition.elements;
            foreach (var order in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}})
            {
                var loadout = new ElementLoadout { q = e[order[0]], e = e[order[1]], r = e[order[2]] };
                Check(catalog.For(loadout) == definition, "order-independent ultimate " + definition.kind);
                Check(player.GetComponentInChildren<SpellManager>().Spells.Count(s => s.IsAvailable(loadout)) == 8, "eight ordinary skills " + definition.kind);
            }
            if (definition.worldPrefab != null) Check(definition.worldPrefab.GetComponent<NetworkIdentity>().assetId != 0, "network prefab " + definition.kind);
        }
        Check(catalog.Get(UltimateKind.SteamFlight).duration == 15 && catalog.Get(UltimateKind.HeatDrain).duration == 7, "approved durations");
        Check(catalog.orbStun == 3 && catalog.orbBlastDamage == 40 && catalog.orbBlastRadius == 4, "approved glacier explosion");
        Check(catalog.meteorHealth == 500 && catalog.rebirthDelay == 4, "approved phoenix parameters");
        Check(catalog.laserBodyDamage == 80 && catalog.laserHeadMultiplier == 1.5f && catalog.laserInterval == .7f, "approved laser parameters");
        results.Add("PASS: catalog, all 60 key permutations, eight ordinary spells, prefab IDs and agreed values.");
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (started == 0) { started = now; next = now + 1; Application.logMessageReceived += OnLog; }
            if (now - started > 120) throw new Exception("Timed out at stage " + stage);
            if (now < next) return;
            next = now + .15;
            if (stage == 0)
            {
                manager = NetManager.Room;
                if (manager == null) return;
                ((PortTransport)manager.transport).Port = 17994;
                manager.ConfigureRoom("Ultimate regression", new MatchRules { minutes = 30, killGoal = 33 }, "");
                manager.StartHost(); stage = 1; next = now + 1; return;
            }
            if (stage == 1)
            {
                if (NetworkClient.localPlayer == null) return;
                owner = NetworkClient.localPlayer.GetComponentInChildren<PlayerUltimate>();
                if (owner == null || !owner.GetComponent<PlayerNetworkCaster>().LoadoutReady) return;
                catalog = owner.catalog;
                owner.GetComponent<RelativeMovement>().enabled = false;
                owner.GetComponent<SpellManager>().enabled = false;
                owner.enabled = false;
                owner.GetComponent<RelativeMovement>().ServerTeleport(Origin, Quaternion.identity);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Ultimate regression floor";
                floor.transform.position = Origin + Vector3.down * 1.25f; floor.transform.localScale = new Vector3(60,.5f,60);
                temporary.Add(floor); Physics.SyncTransforms();
                ValidateAssets(); stage = 2; return;
            }
            switch (stage)
            {
                case 2: CheckCharge(); CheckVolley(); break;
                case 3: CheckPhoenix(); break;
                case 4: CheckSpirit(); break;
                case 5: CheckFlight(); break;
                case 6: CheckDrain(); break;
                case 7: CheckLaser(); break;
                case 8: CheckCaldera(); break;
                case 9: CheckMirrors(); break;
                case 10: CheckGravity(); break;
                case 11: CheckGlacier(); break;
                case 12: CheckSecurity(); break;
                case 13:
                    Select(owner, UltimateKind.PrismaticVolley);
                    owner.GetComponent<RelativeMovement>().ServerTeleport(new Vector3(0,4,0), Quaternion.identity);
                    CheckHud();
                    ScreenCapture.CaptureScreenshot("Logs/ultimate-hud-play.png");
                    next = now + 1; break;
                case 14:
                    Check(errors.Count == 0, "No runtime errors: " + string.Join(" | ", errors));
                    Finish(null); return;
            }
            stage++;
        }
        catch (Exception exception) { Finish(exception); }
    }
    static PlayerUltimate Actor(Vector3 position, UltimateKind kind = UltimateKind.PrismaticVolley)
    {
        var root = Object.Instantiate(manager.playerPrefab, position, Quaternion.identity);
        NetworkServer.Spawn(root); temporary.Add(root);
        var actor = root.GetComponentInChildren<PlayerUltimate>();
        actor.GetComponent<RelativeMovement>().enabled = false;
        actor.GetComponent<SpellManager>().enabled = false; actor.enabled = false;
        actor.GetComponent<RelativeMovement>().ServerTeleport(position, Quaternion.identity);
        Select(actor, kind); return actor;
    }
    static void Remove(PlayerUltimate actor)
    {
        actor.ServerEnd(false); NetworkServer.Destroy(actor.netIdentity.gameObject);
    }
    static void Select(PlayerUltimate actor, UltimateKind kind)
    {
        actor.ServerEnd(false);
        var e = actor.catalog.Get(kind).elements;
        Set(actor.GetComponent<PlayerNetworkCaster>(), "loadout", new ElementLoadout { q = e[0], e = e[1], r = e[2] });
        Set(actor.GetComponent<PlayerNetworkCaster>(), "loadoutReady", true);
        Set(actor, "chargePoints", UltimateCatalog.FullChargePoints);
        Set(actor, "nextChargeTick", NetworkTime.time + UltimateCatalog.PassiveChargeInterval);
        actor.ServerSetTeam(-1);
    }
    static Ray Aim(PlayerUltimate actor, Vector3 point) => new Ray(actor.transform.position + Vector3.up, (point - actor.transform.position - Vector3.up).normalized);
    static void CheckCharge()
    {
        Select(owner, UltimateKind.SteamFlight);
        double now = NetworkTime.time;
        Call(owner, "ConsumeCharge", now);
        Ray ray = Aim(owner, Origin + Vector3.forward * 12);
        Check(owner.ChargePercent == 0 && !owner.ChargeReady && !owner.ServerActivate(ray), "empty charge cannot activate");
        Command(owner, "CmdUse", ray.origin, ray.direction);
        Check(!owner.Active, "network command cannot bypass empty charge");
        Call(owner, "TickCharge", now + 2.99);
        Check(owner.ChargePercent == 0, "no passive credit before three seconds");
        Call(owner, "TickCharge", now + 3);
        Check(owner.ChargePercent == 1, "one percent at three seconds");
        Call(owner, "TickCharge", now + 3);
        Check(owner.ChargePercent == 1, "same tick not credited twice");
        Call(owner, "TickCharge", now + 12.1);
        Check(owner.ChargePercent == 4, "skipped frames retain all passive ticks");
        Call(owner, "TickCharge", now + 300);
        Check(owner.ChargeReady && owner.ChargePercent == 100, "five minutes charges from empty");
        Call(owner, "TickCharge", now + 900);
        Check(owner.ChargePercent == 100, "passive charge capped at 100 percent");
        Check(owner.ServerActivate(ray) && owner.ChargePercent == 0, "activation consumes full charge");
        double firstTick = (double)Get(owner, "nextChargeTick");
        Call(owner, "TickCharge", firstTick - .001);
        Check(owner.ChargePercent == 0, "no banked passive ticks after use");
        Call(owner, "TickCharge", firstTick);
        Check(owner.ChargePercent == 1 && owner.Active, "passive charging continues during active ultimate");
        owner.ServerEnd(false);
        Call(owner, "ConsumeCharge", now);
        var target = Actor(Origin + Vector3.right * 20);
        var hp = target.GetComponent<Health>();
        Set(hp, "maxHealth", 5000); Set(hp, "currentHealth", 5000);
        hp.TakeDamage(15, owner.netId);
        Check(owner.ChargePercent == .5f, "small damage retains fractional percent");
        hp.TakeDamage(15, owner.netId);
        Check(owner.ChargePercent == 1, "two 15-damage hits grant exactly one percent");
        hp.TakeDamage(30, owner.netId);
        Call(owner, "TickCharge", now + 3);
        Check(owner.ChargePercent == 3, "damage and passive charging add together");
        owner.GetComponent<Health>().TakeDamage(1, owner.netId); hp.TakeDamage(1);
        Check(owner.ChargePercent == 3, "self and environmental damage grant no charge");
        owner.ServerSetTeam(4); target.ServerSetTeam(4); hp.TakeDamage(30, owner.netId);
        Check(owner.ChargePercent == 3, "allied damage grants no charge");
        target.ServerSetTeam(5); hp.GrantShield(45, 10); hp.TakeDamage(30, owner.netId);
        Check(owner.ChargePercent == 3, "shield absorption grants no charge");
        hp.TakeDamage(30, owner.netId);
        Check(owner.ChargePercent == 3.5f, "partial shield credits only 15 lost HP");
        Set(hp, "currentHealth", 15); hp.TakeDamage(9999, owner.netId);
        Check(owner.ChargePercent == 4, "overkill credits only remaining health");
        hp.TakeDamage(9999, owner.netId);
        Check(owner.ChargePercent == 4, "dead target grants no extra charge");
        Remove(target);
        owner.ServerSetTeam(-1);
        var phoenix = Actor(Origin + Vector3.right * 20, UltimateKind.PhoenixBirth);
        phoenix.ServerActivate(Aim(phoenix, phoenix.transform.position + Vector3.forward * 8));
        Call(owner, "ConsumeCharge", now);
        phoenix.GetComponent<Health>().TakeDamage(1000, owner.netId);
        phoenix.GetComponent<Health>().TakeDamage(60, owner.netId);
        phoenix.GetComponent<Health>().TakeDamage(1000, owner.netId);
        Check(owner.ChargePercent == 20, "100 player HP and 500 meteor HP grant exactly 20 percent");
        Remove(phoenix);
        var spirit = Actor(Origin + Vector3.right * 20, UltimateKind.ElementalSpirit);
        spirit.ServerActivate(Aim(spirit, spirit.transform.position + Vector3.forward * 8));
        spirit.GetComponent<Health>().TakeDamage(30, owner.netId);
        Check(owner.ChargePercent == 21, "golem health damage contributes charge");
        Remove(spirit);
        target = Actor(Origin + Vector3.right * 20);
        hp = target.GetComponent<Health>(); Set(hp, "maxHealth", 5000); Set(hp, "currentHealth", 5000);
        hp.TakeDamage(4000, owner.netId);
        Check(owner.ChargeReady && owner.ChargePercent == 100, "damage charge capped at 100 percent");
        Call(target, "ConsumeCharge", now); Set(target, "chargePoints", 450);
        hp.TakeDamage(1000);
        Check(hp.IsDead && target.ChargePercent == 15, "death retains existing charge");
        Call(target, "ServerTick", now + 3);
        Check(hp.IsDead && target.ChargePercent == 16, "passive tick continues while dead");
        hp.TakeDamage(1000);
        Call(target, "ServerTick", now + 6);
        Check(target.ChargePercent == 17, "repeated damage and death do not reset passive clock");
        hp.Heal(1000);
        Check(target.ChargePercent == 17, "healing does not grant charge");
        Remove(target);
        owner.GetComponent<Health>().Heal(100);
        Call(owner, "ConsumeCharge", now);
        double endsAt = (double)Get(manager, "endsAt");
        try
        {
            Set(manager, "endsAt", NetworkTime.time - 1);
            Call(owner, "TickCharge", now + 30);
            Check(owner.ChargePercent == 0, "no passive charge after match ends");
        }
        finally { Set(manager, "endsAt", endsAt); }
        Call(owner, "TickCharge", now + 30);
        Check(owner.ChargePercent == 0, "inactive match time is not banked");
        results.Add("PASS: charge from 30 damage/percent and each three seconds, fractions, simultaneous sources, caps, activation spending, death persistence, form HP, team/self/shield/overkill filters and inactive match time.");
    }
    static void CheckVolley()
    {
        Select(owner, UltimateKind.PrismaticVolley);
        Check(owner.ServerActivate(Aim(owner, Origin + Vector3.forward*12)), "volley activates");
        var manager = owner.GetComponent<SpellManager>();
        var fire = manager.Spells.OfType<FireBall>().First();
        var ice = manager.Spells.OfType<ElementalSpell>().First(s => s.name == "IceShard");
        Check(owner.GetComponent<PlayerNetworkCaster>().SpawnProjectile(fire.PreviewPrefab, fire.ProjectileSpeed, Vector3.forward), "fire projectile");
        Check(owner.GetComponent<PlayerNetworkCaster>().CastElemental(ice, Vector3.forward), "ice projectile");
        owner.ServerRecordShot(fire.PreviewPrefab, fire.ProjectileSpeed); owner.ServerRecordShot(fire.PreviewPrefab, fire.ProjectileSpeed);
        owner.ServerRecordShot(fire.PreviewPrefab, fire.ProjectileSpeed);
        Check(owner.Charges == 4 && owner.ActiveRemaining > 14.9, "four stored shots capped, fifteen-second window");
        Call(owner, "ServerRecast");
        int copies = Object.FindObjectsByType<FireballProjectile>(FindObjectsSortMode.None).Count(s => s.damageScale == catalog.copyDamage) +
            Object.FindObjectsByType<ElementalEffect>(FindObjectsSortMode.None).Count(s => s.damageScale == catalog.copyDamage);
        Check(copies == 4 && !owner.Active && !owner.ChargeReady, "four weakened copies and spent charge");
        results.Add("PASS: real fire/ice casts record four copies during fifteen seconds, release and preserve normal damage assets.");
    }
    static void CheckPhoenix()
    {
        var actor = Actor(Origin + Vector3.right * 14, UltimateKind.PhoenixBirth);
        var health = actor.GetComponent<Health>(); var stats = actor.GetComponent<PlayerStats>();
        Check(actor.ServerActivate(Aim(actor, actor.transform.position + Vector3.forward*8)), "phoenix aura");
        Check(actor.ChargePercent == 0, "aura activation spends charge");
        Call(actor, "TickCharge", (double)Get(actor, "nextChargeTick"));
        int deaths = stats.Deaths;
        health.TakeDamage(1000, owner.netId);
        Check(actor.IsMeteor && !health.IsDead && health.CurrentHealth == 500 && stats.Deaths == deaths, "lethal hit becomes meteor without a death");
        Check(actor.ActiveRemaining > 3.9 && actor.ChargePercent == 1, "four-second rebirth preserves charge accumulated during aura");
        actor.presentation.Refresh(actor); Physics.SyncTransforms();
        Check(actor.presentation.meteor.activeSelf && ProjectileContact.IsDamageCollider(actor.presentation.meteorCollider, health), "meteor visual and hitbox");
        health.TakeDamage(499, owner.netId);
        Check(health.CurrentHealth == 1 && !health.IsDead, "meteor survives 499 damage");
        Call(actor, "ServerTick", NetworkTime.time + 4.01);
        Check(!actor.Active && !health.IsDead && health.CurrentHealth == 100 && stats.Deaths == deaths, "full health rebirth, no death recorded");
        Select(actor, UltimateKind.PhoenixBirth); actor.ServerActivate(Aim(actor, actor.transform.position + Vector3.forward*8));
        health.TakeDamage(1000, owner.netId); health.TakeDamage(500, owner.netId);
        Check(health.IsDead && stats.Deaths == deaths + 1 && !actor.Active, "destroyed meteor records one real death");
        float charge = actor.ChargePercent;
        health.TakeDamage(500, owner.netId);
        Check(stats.Deaths == deaths + 1 && actor.ChargePercent == charge, "no duplicate death, charge persists");
        Remove(actor);
        results.Add("PASS: phoenix aura, 500 HP, four-second full-health return, destructible form, one death and retained charge.");
    }
    static void CheckSpirit()
    {
        var actor = Actor(Origin + Vector3.left*14, UltimateKind.ElementalSpirit);
        var health = actor.GetComponent<Health>(); health.TakeDamage(50);
        Check(actor.ServerActivate(Aim(actor, actor.transform.position + Vector3.forward*8)), "spirit activates");
        actor.presentation.Refresh(actor);
        Check(actor.presentation.spiritModel.GetComponentInChildren<Animation>(true).IsPlaying("Hover"),"golem plays hover on activation");
        CheckSpiritMotion(actor);
        Check(health.CurrentHealth == 200 && actor.BlocksSpells, "spirit health and spell replacement");
        health.TakeDamage(30, owner.netId); actor.ServerEnd(true);
        Check(health.CurrentHealth == 50 && !health.IsDead, "return preserves prior wounds");
        Select(actor, UltimateKind.ElementalSpirit); actor.ServerActivate(Aim(actor, actor.transform.position + Vector3.forward*8));
        health.TakeDamage(200, owner.netId);
        Check(health.IsDead && !actor.Active, "golem death kills its owner");
        Remove(actor); results.Add("PASS: spirit health, wounds on return and lethal golem destruction.");
    }
    static void CheckFlight()
    {
        Select(owner, UltimateKind.SteamFlight); owner.ServerActivate(Aim(owner, Origin + Vector3.forward*8));
        var movement = owner.GetComponent<RelativeMovement>();
        Check(owner.ActiveRemaining > 14.9 && movement.IsFlying && !owner.BlocksSpells, "15-second flight with ordinary spells");
        var input = new RelativeMovement.MoveInput { vertical = 1, forward = Vector3.forward };
        float before = owner.transform.position.y;
        Call(movement, "Simulate", input, .1f, NetworkTime.time);
        Check(owner.transform.position.y > before + .6f, "flight ascent");
        Call(movement, "Simulate", new RelativeMovement.MoveInput(), .1f, NetworkTime.time);
        var flightState = (RelativeMovement.UltimateMotionState)Get(movement,"ultimateMotion");
        Check(owner.transform.position.y >= flightState.launchY-.01f, "automatic launch reaches caster height without held jump");
        flightState.launchUntil = 0; Set(movement,"ultimateMotion",flightState);
        float hovering = owner.transform.position.y; input.vertical = 0;
        Call(movement, "Simulate", input, .1f, NetworkTime.time);
        Check(Mathf.Abs(owner.transform.position.y - hovering) < .01f, "flight hover");
        input.vertical=-1; Call(movement,"Simulate",input,.1f,NetworkTime.time);
        Check(owner.transform.position.y < hovering-.6f,"flight descent");
        var snapshot = (RelativeMovement.MotorState)Call(movement, "CaptureState");
        Check(snapshot.ultimateMotion.flightUntil > NetworkTime.time + 14 && snapshot.ultimateMotion.ceiling == Origin.y + 12, "flight prediction snapshot");
        owner.ServerEnd(false); Check(!movement.IsFlying, "early landing restores gravity");
        movement.ServerTeleport(Origin, Quaternion.identity);
        results.Add("PASS: flight ascent, hover, 15-second duration, altitude ceiling in prediction snapshots, cancellation.");
    }
    static void CheckDrain()
    {
        Select(owner, UltimateKind.HeatDrain); owner.ServerSetTeam(7);
        var enemy = Actor(Origin + Vector3.forward*8); enemy.ServerSetTeam(8);
        var ally = Actor(Origin + Vector3.right*2); ally.ServerSetTeam(7);
        var neutral = Actor(Origin + Vector3.left*2);
        var hp = owner.GetComponent<Health>(); Set(hp, "currentHealth", 10);
        Set(enemy.GetComponent<Health>(), "maxHealth", 5000); Set(enemy.GetComponent<Health>(), "currentHealth", 5000);
        Set(ally.GetComponent<Health>(), "currentHealth", 10); Set(neutral.GetComponent<Health>(), "currentHealth", 10);
        Physics.SyncTransforms();
        Ray ray = Aim(owner, BodyPoint(enemy)); owner.ServerActivate(ray);
        double now = NetworkTime.time;
        Set(owner, "aim", ray); Set(owner, "aimAt", now); Set(owner, "activeUntil", now); Set(owner, "nextDrainTick", now-6.75);
        Call(owner, "TickDrain", now);
        Check(enemy.GetComponent<Health>().CurrentHealth == 4860, "28 exact drain ticks, 140 damage");
        Check(hp.CurrentHealth == 100 && ally.GetComponent<Health>().CurrentHealth == 80 && neutral.GetComponent<Health>().CurrentHealth == 10, "self and fractional ally healing, no FFA healing");
        Set(hp, "currentHealth", 10); enemy.GetComponent<Health>().GrantShield(100, 10);
        Set(owner, "nextDrainTick", now); Call(owner, "TickDrain", now);
        Check(hp.CurrentHealth == 10, "shield absorption gives no life steal");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = Origin + new Vector3(0,1,4); wall.transform.localScale = new Vector3(5,5,.2f); temporary.Add(wall);
        Physics.SyncTransforms(); int shield = enemy.GetComponent<Health>().Shield;
        Set(owner, "nextDrainTick", now); Call(owner, "TickDrain", now);
        Check(enemy.GetComponent<Health>().Shield == shield && hp.CurrentHealth == 10, "cover interrupts damage and healing");
        Object.Destroy(wall); owner.ServerEnd(false); hp.Heal(100); Remove(enemy); Remove(ally); Remove(neutral);
        results.Add("PASS: seven-second drain totals, actual-health damage, 100% self/50% team healing, shield and wall blocking.");
    }
    static void CheckLaser()
    {
        Select(owner, UltimateKind.PolarPiercer);
        var enemy = Actor(Origin + Vector3.forward*10);
        var hp = enemy.GetComponent<Health>(); Set(hp, "maxHealth", 1000); Set(hp, "currentHealth", 1000);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = Origin + new Vector3(0,1,4); wall.transform.localScale = new Vector3(5,5,.2f); temporary.Add(wall);
        Physics.SyncTransforms();
        var ray = Aim(owner, BodyPoint(enemy)); owner.ServerActivate(ray);
        Command(owner, "CmdPrimary", ray.origin, ray.direction, false);
        Check(hp.CurrentHealth == 920 && owner.Charges == 2, "80 body damage through wall");
        Command(owner, "CmdPrimary", ray.origin, ray.direction, false);
        Check(hp.CurrentHealth == 920 && owner.Charges == 2, "shot interval cannot be bypassed");
        Set(owner, "nextActionAt", 0d);
        var appearance = enemy.GetComponent<WizardAppearance>();
        var head = enemy.GetComponentsInChildren<Collider>().First(c => appearance.IsHeadCollider(c));
        ray = Aim(owner, head.bounds.center);
        Command(owner, "CmdPrimary", ray.origin, ray.direction, false);
        Check(hp.CurrentHealth == 800 && owner.Charges == 1, "fixed 120 head damage through wall");
        Set(owner, "nextActionAt", 0d);
        Command(owner, "CmdPrimary", ray.origin, new Vector3(float.NaN,0,0), false);
        Check(owner.Charges == 1, "invalid aim rejected");
        Command(owner, "CmdPrimary", ray.origin, Vector3.right, false);
        Check(!owner.Active && !owner.ChargeReady, "miss consumes final shot without refunding ultimate charge");
        Object.Destroy(wall); Remove(enemy);
        results.Add("PASS: wall piercing, 80 body / 120 animated-head damage, shot interval, finite-input validation, three-charge limit.");
    }
    static UltimateWorldEffect Ground(UltimateKind kind)
    {
        Select(owner, kind);
        var ray = Aim(owner, Origin + new Vector3(9,-1,9));
        Check(owner.ServerActivate(ray), kind + " valid ground placement: " + owner.LastFailure);
        return (UltimateWorldEffect)Get(owner, "world");
    }
    static void CheckCaldera()
    {
        var effect = Ground(UltimateKind.EarthDepths);
        Check(Vector3.ProjectOnPlane(effect.transform.position-owner.transform.position,Vector3.up).sqrMagnitude < .01f,"island appears directly under caster");
        Check(effect.GetComponentsInChildren<MeshCollider>().Length == 1,"walkable island surface");
        var movement=owner.GetComponent<RelativeMovement>();
        double start=NetworkTime.time; float initialY=owner.transform.position.y;
        for(int i=1;i<=65;i++)
        {
            double now=start+i*.02; Call(effect,"StepIsland",now); Physics.SyncTransforms();
            Call(movement,"Simulate",new RelativeMovement.MoveInput(),.02f,now);
        }
        Check(owner.transform.position.y > initialY+4.8f,"rising island carries caster with prediction motor");
        var enemy = Actor(effect.transform.TransformPoint(new Vector3(0,1,2)));
        Physics.SyncTransforms(); int before = enemy.GetComponent<Health>().CurrentHealth;
        Call(effect, "ApplyLava"); Check(enemy.GetComponent<Health>().CurrentHealth == before-6, "island heat deals 12 DPS");
        Set(effect,"islandStarted",NetworkTime.time-2);
        Call(owner,"ServerRecast"); Check(owner.Active && effect.Collapsing,"F starts physical drop without immediate explosion");
        enemy.GetComponent<RelativeMovement>().ServerTeleport(Origin+Vector3.forward*2,Quaternion.identity);
        Physics.SyncTransforms(); before=enemy.GetComponent<Health>().CurrentHealth;
        Call(effect,"StepIsland",NetworkTime.time+2);
        Check(!owner.Active && enemy.GetComponent<Health>().CurrentHealth==before-catalog.collapseDamage,"island explodes on impact");
        Remove(enemy); movement.ServerTeleport(Origin,Quaternion.identity);
        results.Add("PASS: island under caster, rising platform carry, damage, F starts fall, impact explosion.");
    }
    static void CheckMirrors()
    {
        var effect = Ground(UltimateKind.MirrorLabyrinth);
        Check(effect.IntactMirrors==0 && effect.mirrors.All(m=>!m.gameObject.activeSelf),"no automatic mirror placement");
        for(int i=0;i<6;i++)
        {
            float angle=i*Mathf.PI/3;
            var ray=Aim(owner,Origin+new Vector3(Mathf.Cos(angle)*8,-1,Mathf.Sin(angle)*8));
            Set(owner,"nextCommandAt",0d);
            Check(owner.ServerPlaceMirror(ray,i*15),"place individual mirror "+i);
        }
        Check(owner.Charges==6 && effect.mirrorPoses.Count==6 && effect.mirrorPoses[5].yaw==75,"six independent synchronized positions and rotations");
        Set(owner,"nextCommandAt",0d);
        Check(!owner.ServerPlaceMirror(Aim(owner,Origin+new Vector3(-4,-1,4)),90),"seventh mirror rejected");
        Check(owner.ActiveRemaining>26.9 && owner.ActiveRemaining<=27.01,"full 27-second window after final placement");
        var panel = effect.mirrors[0];
        var root = new GameObject("Mirror projectile probe", typeof(Rigidbody), typeof(SphereCollider)); temporary.Add(root);
        root.GetComponent<SphereCollider>().radius = .1f;
        root.transform.position = panel.transform.position + panel.transform.forward;
        root.GetComponent<Rigidbody>().useGravity = false; root.GetComponent<Rigidbody>().linearVelocity = -panel.transform.forward * 10;
        Check(UltimateMirror.TryReflect(panel.GetComponent<Collider>(), root.transform, owner.netId, out uint reflected), "mirror reflects owner's projectile");
        Check(reflected == owner.netId && Vector3.Dot(root.GetComponent<Rigidbody>().linearVelocity, panel.transform.forward) > 9, "specular reflection preserves author");
        effect.DamageMirror(0, 100);
        Check((effect.IntactMirrors & 1) == 0 && !panel.gameObject.activeSelf, "destroyed panel mask and collider");
        owner.ServerEnd(false); Object.Destroy(root);
        results.Add("PASS: six mirrors, specular trajectories for both sides, preserved ownership and synchronized destruction mask.");
    }
    static void CheckGravity()
    {
        var effect = Ground(UltimateKind.GravityInversion);
        var enemy = Actor(effect.transform.position + Vector3.up);
        Physics.SyncTransforms(); Call(effect, "LiftTargets");
        var movement = enemy.GetComponent<RelativeMovement>();
        var input = new RelativeMovement.MoveInput { forward = Vector3.forward };
        for (int i=0;i<180;i++) Call(movement, "Simulate", input, .02f, NetworkTime.time);
        Check(enemy.transform.position.y > effect.transform.position.y+11 && catalog.gravityRadius==12, "expanded gravity field raises target to twelve meters");
        Check(effect.RequestSlam() && !effect.RequestSlam(), "single warning before slam");
        Set(owner,"activeUntil",NetworkTime.time-.01);
        Call(owner,"ServerTick",NetworkTime.time);
        Check(owner.Active && !effect.Finished, "natural expiry preserves a pending slam warning");
        effect.ServerTick(effect.ExpiresAt);
        int before = enemy.GetComponent<Health>().CurrentHealth;
        for (int i=0;i<80;i++) Call(movement, "Simulate", input, .02f, NetworkTime.time);
        Check(enemy.GetComponent<Health>().CurrentHealth == before-catalog.landingDamage, "one landing damage event");
        Remove(enemy); results.Add("PASS: low-gravity lift, warning, downward slam and exactly one landing hit.");
    }
    static void CheckGlacier()
    {
        Select(owner, UltimateKind.GlacierRam);
        var enemy = Actor(Origin + Vector3.forward*7);
        Ray ray = Aim(owner, Origin + Vector3.forward*20);
        Check(owner.ServerActivate(ray), "orb starts in free volume");
        var effect = (UltimateWorldEffect)Get(owner, "world");
        Check(owner.ControlledOrb==effect,"owner resolves controlled sphere for camera");
        var motor=owner.GetComponent<RelativeMovement>();
        Vector3 casterPosition=owner.transform.position;Quaternion casterRotation=owner.transform.rotation;
        Set(motor,"iceVelocity",Vector3.right*10);
        Call(motor,"Simulate",new RelativeMovement.MoveInput{movement=Vector3.right,forward=Vector3.right,jump=true},.1f,NetworkTime.time);
        Check(Vector3.ProjectOnPlane(owner.transform.position-casterPosition,Vector3.up).sqrMagnitude<.0001f && Quaternion.Angle(casterRotation,owner.transform.rotation)<.01f,"orb control keeps caster position and rotation fixed even on ice");
        Check((bool)Call(owner,"ValidAim",effect.transform.position+Vector3.back*6.5f,Vector3.forward),"orb camera origin accepted");
        Check(!(bool)Call(owner,"ValidAim",effect.transform.position+Vector3.back*20,Vector3.forward),"remote arbitrary camera origin rejected");
        CheckGlacierGroundMotion(effect, ray);
        CheckGlacierJumpAndRoll(effect);
        effect.SetSteering(Vector3.forward, NetworkTime.time);
        for (int i=0;i<6;i++) { effect.StepOrb(.1f); Physics.SyncTransforms(); }
        Check(enemy.GetComponent<Health>().CurrentHealth < 100, "continuous rolling contact");
        effect.transform.position = enemy.transform.position + Vector3.back;
        int before = enemy.GetComponent<Health>().CurrentHealth;
        effect.ServerFinish(true); effect.ServerFinish(true);
        var movement = enemy.GetComponent<RelativeMovement>();
        Check(enemy.GetComponent<Health>().CurrentHealth == before-40, "one 40-damage explosion");
        Check((double)Get(movement,"stunUntil")-NetworkTime.time > 2.95 && movement.IsStunned, "three-second stun");
        Check(!owner.Active, "orb channel releases caster"); Remove(enemy);
        Check(owner.ControlledOrb==null,"camera follow override clears on detonation");
        results.Add("PASS: guided rolling collision, one final 40 damage / 4 m explosion and three-second stun.");
    }
    static void CheckGlacierGroundMotion(UltimateWorldEffect effect, Ray forward)
    {
        Vector3 initial = effect.transform.position;
        // Reproduce the rounding/contact overlap seen at ground level in the arena.
        effect.transform.position = new Vector3(initial.x, Origin.y - 1 + catalog.orbRadius - .002f, initial.z);
        Physics.SyncTransforms();
        effect.SetSteering(Vector3.forward, NetworkTime.time);
        effect.StepOrb(.1f);
        Check(effect.transform.position.z > initial.z + .8f, "floor contact does not pin the rolling sphere");
        float before = effect.transform.position.z;
        var reverse = new Ray(Origin + Vector3.up, Vector3.back);
        effect.SetSteering(Vector3.back, NetworkTime.time); effect.StepOrb(.1f);
        Check(effect.transform.position.z < before - .8f, "fresh aim reverses sphere on the ground");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(wall);
        wall.transform.position = Origin + new Vector3(0,1,6); wall.transform.localScale = new Vector3(5,6,.05f);
        Physics.SyncTransforms(); effect.SetSteering(Vector3.forward, NetworkTime.time);
        for(int i=0;i<30;i++) effect.StepOrb(.02f);
        Check(effect.transform.position.z < Origin.z + 4.91f && effect.transform.position.z > Origin.z + 4.6f, "sphere stops before a thin wall");
        before = effect.transform.position.z;
        effect.SetSteering(Vector3.back, NetworkTime.time);
        for(int i=0;i<5;i++) effect.StepOrb(.02f);
        Check(effect.transform.position.z < before - .8f, "sphere can steer away after wall contact");
        before = effect.transform.position.z;
        effect.SetSteering(Vector3.forward, NetworkTime.time - 1); effect.StepOrb(.1f);
        Check(Mathf.Abs(effect.transform.position.z - before) < .001f, "expired aim safely stops translation");
        wall.SetActive(false); Object.Destroy(wall);
        effect.transform.position = initial; Set(effect,"fallSpeed",0f); Physics.SyncTransforms();
        results.Add("PASS: glacier floor-contact regression, reversal, thin-wall stop, steering away and stale aim.");
    }
    static void CheckSpiritMotion(PlayerUltimate actor)
    {
        var animator=actor.presentation.spiritModel.GetComponentInChildren<UltimateSpiritAnimator>(true);
        var animation=animator.flightAnimation;var root=animation.gameObject;
        var left=root.transform.Find("Lean/Rig/FrostArm/Gauntlet");var right=root.transform.Find("Lean/Rig/MagmaArm/Gauntlet");
        Vector3 hitboxPosition=actor.presentation.spiritColliders[0].transform.position;
        animation.GetClip("SwingLeft").SampleAnimation(root,.2f);float leftStart=root.transform.InverseTransformPoint(left.position).x;
        animation.GetClip("SwingLeft").SampleAnimation(root,.6f);float leftEnd=root.transform.InverseTransformPoint(left.position).x;
        Check(leftStart < -1 && leftEnd > .2f,"LMB arm sweeps left to right across body");
        animation.GetClip("SwingRight").SampleAnimation(root,.2f);float rightStart=root.transform.InverseTransformPoint(right.position).x;
        animation.GetClip("SwingRight").SampleAnimation(root,.6f);float rightEnd=root.transform.InverseTransformPoint(right.position).x;
        Check(rightStart > 1 && rightEnd < -.2f,"RMB arm sweeps right to left across body");
        Check(actor.presentation.spiritColliders[0].transform.position==hitboxPosition,"animated swings do not move hitbox");
        var leg=root.transform.Find("Lean/Rig/LeftLeg");
        animation.GetClip("Walk").SampleAnimation(root,0);Quaternion a=leg.localRotation;
        animation.GetClip("Walk").SampleAnimation(root,.45f);
        Check(Quaternion.Angle(a,leg.localRotation)>60,"walking alternates the legs");
        animation.GetClip("Hover").SampleAnimation(root,0);
        Check(Mathf.Abs(Mathf.DeltaAngle(left.parent.localEulerAngles.y,0))<.01f,"idle restores arm after swing");
        // Exercise the RPC receiver here; end-to-end dispatch is checked in the two-process test.
        Command(actor,"RpcSpiritStrike",false,NetworkTime.time);
        Check(animator.CurrentMotion=="SwingLeft","left-swing RPC selects correct animation");
        Command(actor,"RpcSpiritStrike",true,NetworkTime.time);
        Check(animator.CurrentMotion=="SwingRight","right-swing RPC selects correct animation");
        results.Add("PASS: golem walk cycle, mirrored sweeping attacks, RPC receiver and stable hitboxes.");
    }
    static void CheckGlacierJumpAndRoll(UltimateWorldEffect effect)
    {
        Vector3 original=effect.transform.position;
        effect.transform.position=new Vector3(original.x,Origin.y-1+catalog.orbRadius+.03f,original.z);
        effect.SetSteering(Vector3.zero,NetworkTime.time);Set(effect,"fallSpeed",0f);Physics.SyncTransforms();
        float floorY=effect.transform.position.y;
        Check(effect.TryJumpOrb() && !effect.TryJumpOrb(),"ram can jump once from ground");
        effect.StepOrb(.1f);Physics.SyncTransforms();
        Check(effect.transform.position.y>floorY+.6f,"jump lifts sphere instead of snapping to floor");
        Set(effect,"nextOrbJump",0d);
        Check(!effect.TryJumpOrb(),"midair jump rejected");
        float peak=effect.transform.position.y;
        for(int i=0;i<90;i++){effect.StepOrb(.02f);Physics.SyncTransforms();peak=Mathf.Max(peak,effect.transform.position.y);}
        Check(peak>floorY+1.8f && Mathf.Abs(effect.transform.position.y-floorY)<.02f,"jump arc returns to ground");
        Check(effect.TryJumpOrb(),"jump available again after landing");
        var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(ceiling);
        ceiling.transform.position=effect.transform.position+Vector3.up*1.6f;ceiling.transform.localScale=new Vector3(5,.15f,5);Physics.SyncTransforms();
        float maximumCenter=ceiling.GetComponent<Collider>().bounds.min.y-catalog.orbRadius;
        for(int i=0;i<8;i++){effect.StepOrb(.02f);Physics.SyncTransforms();}
        Check(effect.transform.position.y<=maximumCenter+.01f,"jump stops at low ceiling");
        ceiling.SetActive(false);Object.Destroy(ceiling);
        var model=effect.rotatingVisual;model.rotation=Quaternion.identity;
        Set(effect,"rollReady",true);Set(effect,"displayReady",true);Set(effect,"lastRollPosition",effect.transform.position);
        Vector3 start=effect.transform.position;effect.transform.position=start+Vector3.forward;Set(effect,"previousPosition",effect.transform.position);Call(effect,"LateUpdate");
        Check(Quaternion.Angle(model.rotation,Quaternion.AngleAxis(Mathf.Rad2Deg/catalog.orbRadius,Vector3.right))<.01f,"forward travel rolls around correct world axis");
        effect.transform.position=start;Set(effect,"previousPosition",start);Call(effect,"LateUpdate");
        Check(Quaternion.Angle(model.rotation,Quaternion.identity)<.01f,"reversing travel reverses rolling");
        Call(effect,"LateUpdate");Check(Quaternion.Angle(model.rotation,Quaternion.identity)<.01f,"stationary sphere stops spinning");
        effect.transform.position=original;Set(effect,"fallSpeed",0f);Set(effect,"nextOrbJump",0d);Set(effect,"rollReady",false);Physics.SyncTransforms();
        results.Add("PASS: ram ground jump, no double jump, landing, ceiling collision and travel-based rolling.");
    }
    static void CheckSecurity()
    {
        Select(owner, UltimateKind.HeatDrain);
        Ray ray = Aim(owner, Origin + Vector3.forward*8);
        Check(owner.ServerActivate(ray), "channel starts");
        var caster = owner.GetComponent<PlayerNetworkCaster>();
        int count = Object.FindObjectsByType<FireballProjectile>(FindObjectsSortMode.None).Length;
        for(int i=0;i<3;i++) Command(caster,"CmdSubmitElement",0,ray.origin,ray.direction,Vector3.zero);
        Check(Object.FindObjectsByType<FireballProjectile>(FindObjectsSortMode.None).Length == count, "server blocks ordinary spells during channel");
        owner.GetComponent<RelativeMovement>().ServerStun(1);
        Call(owner,"ServerTick",NetworkTime.time);
        Check(!owner.Active && !owner.ChargeReady, "stun cancels channel without refund");
        owner.GetComponent<RelativeMovement>().ResetVerticalVelocity();
        Select(owner, UltimateKind.GravityInversion);
        Check(!owner.ServerActivate(new Ray(Origin+Vector3.up,Vector3.up)) && owner.ChargeReady, "invalid ground target does not consume charge");
        results.Add("PASS: server spell/channel exclusivity, stun cancellation, rejected placement without charge loss.");
    }
    static void CheckHud()
    {
        var ui = Object.FindFirstObjectByType<PlayerGameUI>();
        Set(ui,"built",false); Call(ui,"Update");
        var hud = Object.FindFirstObjectByType<UltimateHUD>();
        Check(hud != null, "central saved HUD exists");
        var visible = hud.transform.parent.Cast<Transform>().Where(t=>t.gameObject.activeSelf).ToArray();
        Check(visible.Length == 9 && visible[4] == hud.transform, "four ordinary cards on each side");
        Set(owner, "chargePoints", 1499); Call(hud, "Update");
        Check(hud.timer.text == "49%" && Mathf.Abs(hud.fill.fillAmount - (1 - 1499f/3000)) < .0001f, "HUD percent and fill preserve fractional progress");
        Set(owner, "chargePoints", UltimateCatalog.FullChargePoints); Call(hud, "Update");
        Check(hud.timer.text == "100%" && hud.fill.fillAmount == 0, "HUD shows fully charged ultimate");
        results.Add("PASS: nine saved HUD slots, ultimate centered between four ordinary cards on each side.");
    }
    static Vector3 BodyPoint(PlayerUltimate actor)
    {
        var appearance = actor.GetComponent<WizardAppearance>();
        return actor.GetComponentsInChildren<Collider>().First(c=>appearance.IsDamageCollider(c)&&!appearance.IsHeadCollider(c)).bounds.center;
    }
    static object Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,Private).Invoke(target,args);
    static object Get(object target,string name) => target.GetType().GetField(name,Private).GetValue(target);
    static void Set(object target,string name,object value) => target.GetType().GetField(name,Private).SetValue(target,value);
    static void Command(object target,string name,params object[] args) => target.GetType().GetMethods(Private).Single(m=>m.Name.StartsWith("UserCode_"+name+"__")).Invoke(target,args);
    static void Check(bool condition,string message) { if(!condition)throw new Exception(message); assertions++; }
    static void OnLog(string message,string trace,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(message); }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Flag,false); SessionState.SetBool(Flag+".Restore",true);
        Application.logMessageReceived -= OnLog;
        if(error!=null)results.Add("FAIL stage "+stage+": "+error);
        results.Add("Assertions: "+assertions); results.AddRange(errors.Select(e=>"ERROR: "+e));
        results.Add(DateTime.UtcNow.ToString("O"));
        File.WriteAllLines("Logs/ultimate-validation.txt",results);
        foreach(var root in temporary)if(root!=null && root.GetComponent<NetworkIdentity>()==null)Object.Destroy(root);
        if(manager!=null && NetworkServer.active)manager.StopHost();
        EditorApplication.isPlaying=false;
    }
}
