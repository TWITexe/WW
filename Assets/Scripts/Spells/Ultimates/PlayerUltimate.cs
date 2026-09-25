using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

// Commands carry input only. The server owns selection, charge progress, damage and lifetime.
[DefaultExecutionOrder(210)]
public partial class PlayerUltimate : NetworkBehaviour
{
    public UltimateCatalog catalog;
    public UltimatePresentation presentation;
    [SyncVar] private bool active;
    [SyncVar] private UltimateKind activeKind;
    [SyncVar] private double activeUntil;
    // Integer damage units retain fractions of a percent without floating-point drift.
    [SyncVar] private int chargePoints;
    private double nextChargeTick;
    [SyncVar] private int charges;
    [SyncVar] private byte phase;
    [SyncVar(hook = nameof(OnFormHealthChanged))] private int formHealth;
    [SyncVar] private Vector3 beamEnd;
    [SyncVar] private bool beamHit;
    [SyncVar] private int teamId = -1;
    private Health health;
    private PlayerNetworkCaster caster;
    private RelativeMovement movement;
    private Ray aim;
    private double aimAt, nextAimSend, nextAimReceive, nextActionAt, nextCommandAt, nextDrainTick;
    private UltimateWorldEffect world;
    [SyncVar] private uint worldNetId;
    private float mirrorYaw;
    private bool targeting;
    private double targetingUntil;
    private LineRenderer targetRing;
    private int healthBeforeSpirit;
    private readonly Dictionary<Health, float> healingRemainders = new Dictionary<Health, float>();
    private struct StoredShot { public GameObject prefab; public float speed; }
    private readonly List<StoredShot> storedShots = new List<StoredShot>(UltimateCatalog.VolleyCapacity);
    public UltimateWorldEffect WorldEffect
    {
        get
        {
            if (isServer) return world;
            return worldNetId != 0 && NetworkClient.spawned.TryGetValue(worldNetId, out var identity)
                ? identity.GetComponent<UltimateWorldEffect>() : null;
        }
    }
    public UltimateWorldEffect ControlledOrb => active && Kind == UltimateKind.GlacierRam ? WorldEffect : null;

    public UltimateDefinition Definition => catalog == null || caster == null || !caster.LoadoutReady ? null : catalog.For(caster.Loadout);
    public UltimateKind Kind => active ? activeKind : Definition != null ? Definition.kind : UltimateKind.PrismaticVolley;
    public bool Active => active;
    public int Charges => charges;
    public byte Phase => phase;
    public float ChargePercent => chargePoints / (float)UltimateCatalog.DamagePerChargePercent;
    public bool ChargeReady => chargePoints >= UltimateCatalog.FullChargePoints;
    public double ActiveRemaining => Math.Max(0, activeUntil - NetworkTime.time);
    public bool IsMeteor => active && activeKind == UltimateKind.PhoenixBirth && phase == 1;
    public bool IsSpirit => active && activeKind == UltimateKind.ElementalSpirit;
    public bool HasForm => IsMeteor || IsSpirit;
    public int FormHealth => formHealth;
    public int FormMaxHealth => IsMeteor ? catalog.meteorHealth : catalog.spiritHealth;
    public Vector3 BeamEnd => beamEnd;
    public bool BeamHit => beamHit;
    public bool BlocksSpells => active && (IsMeteor || IsSpirit || Kind == UltimateKind.HeatDrain ||
        Kind == UltimateKind.GlacierRam || Kind == UltimateKind.PolarPiercer);
    public bool IsTargeting => targeting;
    public int TeamId => teamId;
    public string LastFailure { get; private set; }
    public string Caption
    {
        get
        {
            if (targeting) return Definition.kind == UltimateKind.MirrorLabyrinth ?
                $"Зеркала · {charges}/6 · ЛКМ — поставить · колесо — повернуть · ПКМ — убрать прицел" : Definition.title + " · ЛКМ — применить · ПКМ — отмена";
            if (!active) return null;
            switch (Kind)
            {
                case UltimateKind.PrismaticVolley: return $"Призматический залп · {charges}/4 · F — выпустить";
                case UltimateKind.PhoenixBirth: return IsMeteor ? $"Рождение Феникса · {Mathf.CeilToInt((float)ActiveRemaining)} с" : "Аура Феникса готова";
                case UltimateKind.SteamFlight: return "Паровой двигатель · WASD · Пробел / Shift — высота · F — приземлиться";
                case UltimateKind.ElementalSpirit: return "Дух 3 стихий · ЛКМ — удар слева · ПКМ — удар справа · Пробел — прыжок";
                case UltimateKind.HeatDrain: return "Похищение энергии · удерживай прицел на враге · F — завершить";
                case UltimateKind.PolarPiercer: return $"Полярный пробой · {charges}/3 · ЛКМ — выстрел · F — завершить";
                case UltimateKind.GlacierRam: return "Ледниковый таран · WASD — движение · Пробел — прыжок · F / ЛКМ — взорвать";
                case UltimateKind.GravityInversion: return "Гравитационное поле · F — обрушить";
                case UltimateKind.EarthDepths: return "Недра земли · F — обрушить";
                default: return $"Зеркальный лабиринт · {charges}/6 · F — поставить зеркало";
            }
        }
    }

    void Awake()
    {
        health = GetComponent<Health>();
        caster = GetComponent<PlayerNetworkCaster>();
        movement = GetComponent<RelativeMovement>();
    }
    public override void OnStartServer()
    {
        active = false; charges = 0; phase = 0; teamId = -1;
        ConsumeCharge(NetworkTime.time);
    }
    void ConsumeCharge(double now)
    {
        chargePoints = 0;
        nextChargeTick = now + UltimateCatalog.PassiveChargeInterval;
    }
    [Server] void TickCharge(double now)
    {
        if (!NetManager.CombatAllowed)
        {
            nextChargeTick = now + UltimateCatalog.PassiveChargeInterval;
            return;
        }
        if (now < nextChargeTick) return;
        double ticks = Math.Floor((now - nextChargeTick) / UltimateCatalog.PassiveChargeInterval) + 1;
        nextChargeTick += ticks * UltimateCatalog.PassiveChargeInterval;
        chargePoints = Math.Min(UltimateCatalog.FullChargePoints,
            chargePoints + (int)Math.Min(100, ticks) * UltimateCatalog.DamagePerChargePercent);
    }
    [Server] public void ServerCreditDamage(Health target, int actualDamage)
    {
        if (!NetManager.CombatAllowed || actualDamage <= 0 || target == null || Allied(health, target)) return;
        chargePoints += Math.Min(actualDamage, UltimateCatalog.FullChargePoints - chargePoints);
    }
    [Server] public void ServerSetTeam(int value) => teamId = Mathf.Max(-1, value);
    public static bool Allied(Health left, Health right)
    {
        if (left == null || right == null) return false;
        if (left == right) return true;
        var a = left.GetComponent<PlayerUltimate>();
        var b = right.GetComponent<PlayerUltimate>();
        return a != null && b != null && a.teamId >= 0 && a.teamId == b.teamId;
    }
    public bool IsFormCollider(Collider collider) => HasForm && presentation != null && presentation.IsFormCollider(collider, IsMeteor);
    void OnFormHealthChanged(int previous, int current) => health?.NotifyVitalsChanged();

    void Update()
    {
        if (isServer) ServerTick(NetworkTime.time);
        if (presentation != null) presentation.Refresh(this);
    }
    void LateUpdate()
    {
        if (!isLocalPlayer || caster == null || caster.ViewCamera == null || Definition == null) return;
        bool blocked = PlayerGameUI.InputBlocked || health.IsDead || movement.IsStunned;
        if (blocked)
        {
            CancelTargeting();
            if (active && (Kind == UltimateKind.HeatDrain || Kind == UltimateKind.GlacierRam)) CmdCancelChannel();
            return;
        }
        Ray ray = caster.ViewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
        if (active && Kind == UltimateKind.GlacierRam && Input.GetKeyDown(KeyCode.Space)) CmdJumpOrb();
        if (active && NetworkTime.time >= nextAimSend)
        {
            nextAimSend = NetworkTime.time + .05;
            CmdAim(ray.origin, ray.direction);
            if (Kind == UltimateKind.GlacierRam)
            {
                Vector3 forward = Vector3.ProjectOnPlane(ray.direction, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                CmdSteerOrb(Vector3.ClampMagnitude(forward * Input.GetAxisRaw("Vertical") + right * Input.GetAxisRaw("Horizontal"), 1));
            }
        }
        if (targeting)
        {
            if (NetworkTime.time >= targetingUntil || (active && Kind == UltimateKind.MirrorLabyrinth && charges >= 6) || Input.GetMouseButtonDown(1) ||
                Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.R)) { CancelTargeting(); return; }
            bool valid = caster.TryGroundTarget(ray, out Vector3 point);
            bool mirror = Definition.kind == UltimateKind.MirrorLabyrinth;
            if (mirror)
            {
                mirrorYaw = Mathf.Repeat(mirrorYaw + Input.mouseScrollDelta.y * 15, 360);
                valid &= UltimateWorldEffect.MirrorPlacementClear(point, mirrorYaw);
            }
            DrawTarget(point, valid);
            if (valid && Input.GetMouseButtonDown(0))
            {
                if (mirror) CmdPlaceMirror(ray.origin, ray.direction, mirrorYaw);
                else { CmdUse(ray.origin, ray.direction); CancelTargeting(); }
            }
            return;
        }
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (!active && !ChargeReady) return;
            caster.CancelForUltimate();
            if ((!active && UltimateCatalog.IsGroundTargeted(Definition.kind)) || (active && Kind == UltimateKind.MirrorLabyrinth && charges < 6))
            {
                targeting = true; targetingUntil = NetworkTime.time + (Definition.kind == UltimateKind.MirrorLabyrinth ? UltimateCatalog.MirrorPlacementDuration : 5);
                mirrorYaw = transform.eulerAngles.y;
            }
            else CmdUse(ray.origin, ray.direction);
            return;
        }
        if (active && Input.GetMouseButtonDown(0) && (Kind == UltimateKind.PolarPiercer || IsSpirit || Kind == UltimateKind.GlacierRam))
            CmdPrimary(ray.origin, ray.direction, false);
        if (IsSpirit && Input.GetMouseButtonDown(1)) CmdPrimary(ray.origin, ray.direction, true);
    }

    bool ValidAim(Vector3 origin, Vector3 direction) => Finite(origin) && Finite(direction) &&
        direction.sqrMagnitude > .01f && direction.sqrMagnitude < 4 &&
        (origin - (ControlledOrb != null ? ControlledOrb.transform.position : transform.position)).sqrMagnitude <= 100;
    static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    bool CanAct => NetManager.CombatAllowed && health != null && !health.IsDead && movement != null && !movement.IsStunned;

    [Command] void CmdAim(Vector3 origin, Vector3 direction)
    {
        if (!active || !CanAct || NetworkTime.time < nextAimReceive || !ValidAim(origin, direction)) return;
        nextAimReceive = NetworkTime.time + .025;
        aim = new Ray(origin, direction.normalized); aimAt = NetworkTime.time;
    }
    [Command] void CmdUse(Vector3 origin, Vector3 direction)
    {
        if (!CanAct || Definition == null || !ValidAim(origin, direction) || NetworkTime.time < nextCommandAt) return;
        nextCommandAt = NetworkTime.time + .15;
        aim = new Ray(origin, direction.normalized); aimAt = NetworkTime.time;
        if (active) { ServerRecast(); return; }
        if (!ChargeReady) return;
        ServerActivate(aim);
    }
    [Command] void CmdPrimary(Vector3 origin, Vector3 direction, bool alternate)
    {
        if (!active || !CanAct || !ValidAim(origin, direction)) return;
        aim = new Ray(origin, direction.normalized); aimAt = NetworkTime.time;
        if (Kind == UltimateKind.PolarPiercer && !alternate) ServerLaser();
        else if (IsSpirit) ServerSpiritAttack(alternate);
        else if (Kind == UltimateKind.GlacierRam && !alternate) ServerRecast();
    }
    [Command] void CmdCancelChannel()
    {
        if (active && (Kind == UltimateKind.HeatDrain || Kind == UltimateKind.GlacierRam)) ServerEnd(false);
    }

    [Server] public bool ServerActivate(Ray ray)
    {
        var definition = Definition;
        if (!CanAct || active || definition == null || !ChargeReady || !ValidAim(ray.origin, ray.direction)) return false;
        Vector3 point = transform.position;
        if (UltimateCatalog.IsGroundTargeted(definition.kind) && !caster.TryGroundTarget(ray, out point)) { LastFailure = "No reachable ground within 18 m."; return false; }
        if (definition.worldPrefab != null && !TryCreateWorld(definition, point, ray)) return false;
        caster.ServerCancelForUltimate();
        aim = new Ray(ray.origin, ray.direction.normalized); aimAt = NetworkTime.time;
        activeKind = definition.kind; active = true; phase = 0; charges = 0;
        activeUntil = NetworkTime.time + definition.duration;
        ConsumeCharge(NetworkTime.time);
        nextActionAt = nextFrostAt = NetworkTime.time; nextDrainTick = NetworkTime.time + .25;
        beamEnd = ray.GetPoint(10);
        storedShots.Clear(); healingRemainders.Clear();
        switch (Kind)
        {
            case UltimateKind.PhoenixBirth: activeUntil = 0; break;
            case UltimateKind.PolarPiercer: charges = 3; break;
            case UltimateKind.SteamFlight: movement.ServerFlight(definition.duration, catalog.flightHeight, catalog.flightSpeed, catalog.flightVerticalSpeed); break;
            case UltimateKind.ElementalSpirit:
                healthBeforeSpirit = health.CurrentHealth;
                formHealth = catalog.spiritHealth;
                movement.ServerSpirit(definition.duration);
                health.NotifyVitalsChanged();
                break;
            case UltimateKind.GlacierRam: movement.ServerRoot(definition.duration, false); break;
        }
        RpcBurst(transform.position + Vector3.up, definition.color, 1, SpellHitKind.Holy);
        return true;
    }

    [Server] void ServerTick(double now)
    {
        // Accumulation continues with no active ultimate and while waiting to respawn.
        TickCharge(now);
        if (!active) return;
        if (!NetManager.CombatAllowed || health.IsDead) { ServerEnd(false); return; }
        if (movement.IsStunned && (Kind == UltimateKind.HeatDrain || Kind == UltimateKind.GlacierRam)) { ServerEnd(false); return; }
        if (IsMeteor)
        {
            if (now >= activeUntil)
            {
                ServerEnd(false);
                health.ServerRestoreAfterUltimate(health.BaseMaxHealth);
                RpcBurst(transform.position + Vector3.up, new Color(1, .45f, .06f), 2, SpellHitKind.Fire);
            }
            return;
        }
        if (Kind == UltimateKind.PhoenixBirth) return;
        if (Kind == UltimateKind.HeatDrain) TickDrain(now);
        if (world != null) world.SetAim(aim, aimAt);
        if (now < activeUntil) return;
        if (Kind == UltimateKind.PrismaticVolley && storedShots.Count > 0 && now - aimAt < .5) ReleaseVolley();
        if (world != null && Kind == UltimateKind.EarthDepths)
        {
            world.RequestCollapse(); activeUntil = world.ExpiresAt; return;
        }
        if (world != null && Kind == UltimateKind.GravityInversion)
        {
            if (world.RequestSlam() || world.SlamPending) activeUntil = world.ExpiresAt;
            else ServerEnd(false);
            return;
        }
        ServerEnd(true);
    }

    [Server] void ServerRecast()
    {
        if (IsMeteor || Kind == UltimateKind.PhoenixBirth || Kind == UltimateKind.MirrorLabyrinth || IsSpirit) return;
        if (Kind == UltimateKind.PrismaticVolley) ReleaseVolley();
        if (Kind == UltimateKind.EarthDepths && world != null)
        {
            world.RequestCollapse(); activeUntil = world.ExpiresAt; return;
        }
        if (Kind == UltimateKind.GravityInversion && world != null)
        {
            if (world.RequestSlam()) activeUntil = world.ExpiresAt;
            return;
        }
        ServerEnd(true);
    }
    [Server] public void ServerEnd(bool detonate)
    {
        if (!active && world == null) return;
        bool spirit = IsSpirit;
        int restored = spirit ? Mathf.Min(healthBeforeSpirit, Mathf.CeilToInt(health.BaseMaxHealth * formHealth / (float)catalog.spiritHealth)) : 0;
        active = false; phase = 0; charges = 0; beamHit = false;
        movement?.ServerEndUltimateMotion();
        storedShots.Clear(); healingRemainders.Clear();
        var effect = world; world = null; worldNetId = 0;
        if (effect != null) effect.ServerFinish(detonate);
        if (spirit && !health.IsDead && restored > 0) health.ServerRestoreAfterUltimate(restored);
        health?.NotifyVitalsChanged();
    }
    [Server] public void WorldFinished(UltimateWorldEffect effect)
    {
        if (world != effect) return;
        world = null; worldNetId = 0; ServerEnd(false);
    }
    [Server] public bool ServerPreventDeath()
    {
        if (!active || Kind != UltimateKind.PhoenixBirth || phase != 0) return false;
        phase = 1; formHealth = catalog.meteorHealth;
        activeUntil = NetworkTime.time + catalog.rebirthDelay;
        caster.ServerCancelForUltimate();
        movement.ServerRoot(catalog.rebirthDelay, true);
        health.NotifyVitalsChanged();
        RpcBurst(transform.position + Vector3.up, new Color(1, .35f, .04f), 2, SpellHitKind.Fire);
        return true;
    }
    [Server] public int ServerDamageForm(int damage, uint attacker)
    {
        int actual = Mathf.Min(formHealth, Mathf.Max(0, damage));
        formHealth -= actual;
        health.NotifyVitalsChanged();
        if (formHealth <= 0)
        {
            ServerEnd(false);
            health.ServerUltimateDeath(attacker);
        }
        return actual;
    }
    [Server] public void ServerHealForm(int amount)
    {
        if (!IsSpirit || amount <= 0) return;
        formHealth = Mathf.Min(catalog.spiritHealth, formHealth + amount);
        health.NotifyVitalsChanged();
    }
    public override void OnStopServer()
    {
        if (world != null && NetworkServer.active) world.ServerFinish(false);
        world = null;
    }
    public override void OnStopClient() => CancelTargeting();
    void OnDestroy() => CancelTargeting();
    void CancelTargeting()
    {
        targeting = false;
        if (targetRing != null) Destroy(targetRing.gameObject);
        targetRing = null;
    }
    void DrawTarget(Vector3 point, bool valid)
    {
        if (targetRing == null)
        {
            var root = new GameObject("Ultimate target"); root.layer = 2;
            targetRing = root.AddComponent<LineRenderer>();
            targetRing.sharedMaterial = Resources.Load<Material>("UltimateBeam");
            targetRing.positionCount = 64; targetRing.loop = true; targetRing.widthMultiplier = .1f;
        }
        targetRing.enabled = true;
        targetRing.startColor = targetRing.endColor = valid ? Definition.color : new Color(1, .15f, .1f);
        if (Definition.kind == UltimateKind.MirrorLabyrinth)
        {
            targetRing.positionCount = 8;
            Quaternion rotation = Quaternion.Euler(0, mirrorYaw, 0);
            var footprint = new[] { new Vector3(-1,0,-.12f), new Vector3(1,0,-.12f), new Vector3(1,0,.12f),
                new Vector3(.2f,0,.12f), new Vector3(0,0,1), new Vector3(-.2f,0,.12f), new Vector3(-1,0,.12f), new Vector3(-1,0,-.12f) };
            for (int i = 0; i < footprint.Length; i++) targetRing.SetPosition(i, point + Vector3.up * .08f + rotation * footprint[i]);
            return;
        }
        targetRing.positionCount = 64;
        for (int i = 0; i < 64; i++)
        {
            float angle = i * Mathf.PI * 2 / 64;
            targetRing.SetPosition(i, point + new Vector3(Mathf.Cos(angle) * catalog.gravityRadius, .08f, Mathf.Sin(angle) * catalog.gravityRadius));
        }
    }
    [ClientRpc] public void RpcBurst(Vector3 position, Color color, float radius, SpellHitKind hit)
        => SpellVfx.Impact(position, color, radius, hit);
    [ClientRpc] void RpcLaser(Vector3 start, Vector3 end)
        => UltimatePresentation.FlashBeam(start, end, new Color(.4f, .9f, 1), 3);
}
