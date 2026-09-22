using Mirror;
using UnityEngine;

// сервер управляет фазами; клиенты получают время, положение и фазу, но не наносят урон.
public class AdvancedSpellEffect : NetworkBehaviour
{
    public AdvancedSpell definition;
    [SyncVar] public uint ownerId;
    [SyncVar] public double phaseStarted;
    [SyncVar] public int phase; // 0 — полёт/предупреждение, 1 — область, 2 — расход линзы, 3 — ожидание взрыва.
    [SyncVar] public Vector3 velocity;
    private int ticks;
    private int impactIndex;
    private double spawnedAt;
    private Health ownerHealth;
    private uint initialDamageVersion;
    private bool healingInterrupted;
    private double nextVortexControl;

    // заклинатель передаёт своё здоровье напрямую: сетевой корень игрока может находиться выше компонента здоровья.
    public void SetOwner(Health health)
    {
        ownerHealth = health;
        ownerId = health.netId;
    }

    public override void OnStartServer()
    {
        spawnedAt = phaseStarted = NetworkTime.time;
        if (ownerHealth == null && NetworkServer.spawned.TryGetValue(ownerId, out var owner))
            ownerHealth = owner.GetComponentInChildren<Health>();
        initialDamageVersion = ownerHealth != null ? ownerHealth.DamageVersion : 0;
        if (!definition.IsProjectile && !definition.HasWarning) phase = 1;
    }

    private void Update()
    {
        if (!isServer || definition == null) return;
        double now = NetworkTime.time;
        // ледяное ядро остаётся на месте до взрыва; время действия области начинается только после задержки.
        if (phase == 3)
        {
            if (now >= phaseStarted + definition.impactDelay) ActivateArea(transform.position);
            return;
        }
        // после расходования оставляем короткое время на доставку фазы раскалывания клиентам.
        if (definition.kind == AdvancedSpellKind.SteamLens && phase == 2)
        {
            if (now >= phaseStarted + .4) NetworkServer.Destroy(gameObject);
            return;
        }
        // смерть отменяет подготовку метеорита и лечение, но не уже выпущенную атаку.
        if ((definition.kind == AdvancedSpellKind.ThermalSpring || (definition.kind == AdvancedSpellKind.Meteor && phase == 0)) &&
            (ownerHealth == null || ownerHealth.IsDead)) { NetworkServer.Destroy(gameObject); return; }
        if (phase == 0)
        {
            if (definition.IsProjectile)
            {
                if (now - spawnedAt > 6) NetworkServer.Destroy(gameObject);
                return;
            }
            if (definition.kind == AdvancedSpellKind.ShardVortex && now >= nextVortexControl)
            {
                nextVortexControl = now + .15;
                AffectTargets(0, true);
            }
            if (now >= phaseStarted + definition.delay) ActivateArea(transform.position);
            return;
        }
        ApplyImpactPulses(now - phaseStarted);
        if (definition.kind == AdvancedSpellKind.ThermalSpring && ownerHealth.DamageVersion != initialDamageVersion)
            healingInterrupted = true;
        // отсчёт от начала фазы даёт ровно шесть, три или пять тиков даже при задержке кадра.
        int due = DueTicks(definition, now - phaseStarted);
        while (ticks < due)
        {
            ticks++;
            if (definition.kind == AdvancedSpellKind.ThermalSpring)
            {
                if (!healingInterrupted && InArea(ownerHealth)) ownerHealth.Heal(definition.healPerTick);
            }
            else if (definition.tickDamage > 0) AffectTargets(definition.tickDamage, false);
        }
        if (now >= phaseStarted + definition.duration) NetworkServer.Destroy(gameObject);
    }

    // первый тик наступает после интервала; последний включён в полную длительность области.
    public static int DueTicks(AdvancedSpell spell, double elapsed)
    {
        double interval = System.Math.Max(.1, spell.tickInterval);
        return System.Math.Min((int)System.Math.Round(spell.duration / interval),
            System.Math.Max(0, (int)System.Math.Floor((elapsed + .000001) / interval)));
    }

    // проверка всего пути исключает пролёт быстрого снаряда сквозь тонкую стену.
    private void FixedUpdate()
    {
        if (!isServer || phase != 0 || !definition.IsProjectile) return;
        Vector3 previous = transform.position;
        velocity += Vector3.down * (definition.projectileGravity * Time.fixedDeltaTime);
        transform.position += velocity * Time.fixedDeltaTime;
        if (ProjectileContact.Sweep(transform, ownerId, previous, out var hit, out var point))
        {
            if (TacticalEffect.TryReflect(hit, transform, ownerId, out uint reflected))
            {
                ownerId = reflected;
                return;
            }
            hit.GetComponentInParent<TacticalEffect>()?.ProjectileHit(ownerId);
            if (definition.impactDelay > 0)
            {
                transform.position = point;
                velocity = Vector3.zero;
                phase = 3;
                phaseStarted = NetworkTime.time;
            }
            else ActivateArea(point);
        }
    }

    [Server] private void ActivateArea(Vector3 point)
    {
        if (phase != 0 && phase != 3) return;
        transform.position = point;
        phase = 1;
        phaseStarted = NetworkTime.time;
        velocity = Vector3.zero;
        if (definition.impactOffsets == null || definition.impactOffsets.Length == 0)
        {
            if (definition.impactDamage > 0) AffectTargets(definition.impactDamage, true);
        }
        else ApplyImpactPulses(0);
    }

    // несколько взрывов в одном визуальном эффекте наносят урон каждый в свой момент, даже при задержке кадра.
    [Server] private void ApplyImpactPulses(double elapsed)
    {
        if (definition.impactOffsets == null) return;
        while (impactIndex < definition.impactOffsets.Length && elapsed + .00001 >= definition.impactOffsets[impactIndex])
        {
            impactIndex++;
            AffectTargets(definition.impactDamage,true);
        }
    }

    // размеры проверяем по телу, а не только по точке у ног; укрытия защищают от всех воздействий.
    private bool InArea(Health target)
    {
        if (target == null || target.IsDead) return false;
        var collider = target.GetComponent<CharacterController>();
        if (collider == null) return false;
        Vector3 origin = transform.position + Vector3.up * .15f;
        return (collider.ClosestPoint(origin) - origin).sqrMagnitude <= definition.radius * definition.radius &&
            SpellAreaVisibility.CanReach(origin, collider, transform);
    }

    [Server] private void AffectTargets(int damage, bool control)
    {
        foreach (var target in Health.ServerInstances)
        {
            if (target.netId == ownerId || !InArea(target)) continue;
            if (damage > 0) target.TakeSpellDamage(damage, ownerId);
            if (!control || target.IsDead) continue;
            var movement = target.GetComponent<RelativeMovement>();
            if (movement == null) continue;
            if (definition.slow < 1) movement.ApplySlow(definition.slow, phase == 0 ? .2f : definition.slowDuration);
            if (phase == 1 && definition.stunDuration > 0) movement.ServerStun(definition.stunDuration);
        }
        if (damage > 0) ArenaDestructible.Blast(transform.position + Vector3.up * .15f, definition.radius, damage, transform);
    }
}
