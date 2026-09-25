using System.Collections.Generic;
using Mirror;
using UnityEngine;

public partial class PlayerUltimate
{
    [Server] public void ServerRecordShot(GameObject prefab, float speed)
    {
        if (!active || Kind != UltimateKind.PrismaticVolley || storedShots.Count >= UltimateCatalog.VolleyCapacity || NetworkTime.time >= activeUntil) return;
        storedShots.Add(new StoredShot { prefab = prefab, speed = speed });
        charges = storedShots.Count;
    }
    [Server] void ReleaseVolley()
    {
        Vector3 destination = Trace(aim, 100, false, out _, out _, out Vector3 point) ? point : aim.GetPoint(100);
        for (int index = 0; index < storedShots.Count; index++)
        {
            var shot = storedShots[index];
            Vector3 anchor = caster.ShotOrigin();
            float angle = (index * 90 + 45) * Mathf.Deg2Rad;
            Vector3 offset = transform.right * (Mathf.Cos(angle) * 1.1f) + Vector3.up * (1 + Mathf.Sin(angle) * .6f);
            Vector3 position = anchor + offset;
            // A crystal behind nearby cover cannot spawn its projectile on the far side.
            if (Trace(new Ray(anchor, offset.normalized), offset.magnitude, false, out _, out _, out Vector3 obstruction))
                position = obstruction - offset.normalized * .2f;
            Vector3 direction = (destination - position).normalized;
            if (direction.sqrMagnitude < .01f) direction = transform.forward;
            var root = Instantiate(shot.prefab, position, Quaternion.LookRotation(direction));
            if (root.TryGetComponent<FireballProjectile>(out var fire)) { fire.ownerId = netId; fire.damageScale = catalog.copyDamage; }
            if (root.TryGetComponent<ElementalEffect>(out var ice)) { ice.ownerId = netId; ice.damageScale = catalog.copyDamage; }
            var body = root.GetComponent<Rigidbody>();
            body.useGravity = false; body.linearDamping = 0; body.linearVelocity = direction * shot.speed;
            foreach (var a in GetComponentsInChildren<Collider>())
                foreach (var b in root.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(a, b);
            NetworkServer.Spawn(root);
        }
        storedShots.Clear(); charges = 0;
    }

    // The first valid body hit wins. Piercing skips only world geometry, never nearer players.
    bool Trace(Ray ray, float range, bool piercing, out Health target, out Collider collider, out Vector3 point)
    {
        target = null; collider = null; point = ray.GetPoint(range);
        var hits = Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var other = hit.collider;
            if (other.transform.IsChildOf(transform.root)) continue;
            var candidate = other.GetComponentInParent<Health>();
            if (candidate != null)
            {
                if (candidate.IsDead || !ProjectileContact.IsDamageCollider(other, candidate)) continue;
                target = candidate; collider = other; point = hit.point; return true;
            }
            if (piercing || other.isTrigger || other.GetComponentInParent<FireballProjectile>() != null ||
                other.GetComponentInParent<ElementalEffect>() != null || other.GetComponentInParent<WindFlowProjectile>() != null) continue;
            collider = other; point = hit.point; return true;
        }
        return false;
    }
    [Server] void ServerLaser()
    {
        if (charges <= 0 || NetworkTime.time < nextActionAt || NetworkTime.time >= activeUntil) return;
        nextActionAt = NetworkTime.time + catalog.laserInterval;
        charges--;
        // Start on the view ray at the player's depth, excluding bodies behind the caster/camera.
        float depth = Mathf.Max(0, Vector3.Dot(caster.ShotOrigin() - aim.origin, aim.direction));
        Ray shotRay = new Ray(aim.GetPoint(depth), aim.direction);
        Trace(shotRay, catalog.laserRange, true, out Health target, out Collider collider, out Vector3 point);
        if (target != null && !Allied(health, target))
        {
            bool head = SpellDamage.IsHeadshot(target, collider, point);
            target.TakeDamage(head ? Mathf.RoundToInt(catalog.laserBodyDamage * catalog.laserHeadMultiplier) : catalog.laserBodyDamage, netId, head);
            RpcBurst(point, new Color(.5f, .9f, 1), .7f, SpellHitKind.Snow);
        }
        RpcLaser(caster.ShotOrigin(), point);
        if (charges == 0) ServerEnd(false);
    }

    [Server] void TickDrain(double now)
    {
        Trace(aim, catalog.drainRange + 8, false, out _, out _, out Vector3 aimedPoint);
        Vector3 origin = caster.ShotOrigin();
        Vector3 delta = aimedPoint - origin;
        Ray shotRay = new Ray(origin, delta.sqrMagnitude > .001f ? delta.normalized : aim.direction);
        Trace(shotRay, Mathf.Min(catalog.drainRange, delta.magnitude + .1f), false, out Health target, out Collider obstacle, out Vector3 end);
        beamEnd = end;
        bool fresh = now - aimAt <= .35;
        beamHit = fresh && target != null && !Allied(health, target);
        while (nextDrainTick <= now && nextDrainTick <= activeUntil + .00001)
        {
            nextDrainTick += .25;
            if (!fresh) continue;
            int damage = Mathf.RoundToInt(catalog.drainDamagePerSecond * .25f);
            if (!beamHit)
            {
                if (obstacle != null && target == null) ArenaDestructible.Hit(obstacle, end, damage);
                continue;
            }
            ulong before = target.HealthDamageTotal;
            target.TakeDamage(damage, netId);
            int actual = (int)(target.HealthDamageTotal - before);
            if (actual <= 0) continue;
            health.Heal(actual);
            foreach (var ally in Health.ServerInstances)
            {
                if (ally == health || ally == null || ally.IsDead || !Allied(health, ally) ||
                    (ally.transform.position - transform.position).sqrMagnitude > catalog.healingRadius * catalog.healingRadius) continue;
                healingRemainders.TryGetValue(ally, out float credit);
                credit += actual * catalog.allyHealing;
                int whole = Mathf.FloorToInt(credit + .0001f);
                healingRemainders[ally] = credit - whole;
                ally.Heal(whole);
            }
        }
    }

    private double nextFrostAt;
    [Server] void ServerSpiritAttack(bool frost)
    {
        double now = NetworkTime.time;
        if (now >= activeUntil || now < (frost ? nextFrostAt : nextActionAt)) return;
        if (frost) nextFrostAt = now + catalog.spiritFrostInterval;
        else nextActionAt = now + catalog.spiritStrikeInterval;
        Vector3 forward = Vector3.ProjectOnPlane(aim.direction, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = transform.forward;
        Vector3 center = transform.position + Vector3.up * .35f;
        foreach (var enemy in Health.ServerInstances)
        {
            if (enemy == null || enemy.IsDead || Allied(health, enemy)) continue;
            Vector3 delta = enemy.transform.position - center;
            float along = Vector3.Dot(delta, forward);
            bool inArea = frost ? delta.sqrMagnitude <= 16 : along >= 0 && along <= 9 &&
                Vector3.ProjectOnPlane(delta - forward * along, Vector3.up).sqrMagnitude < 2.25f && Mathf.Abs(delta.y) < 3;
            if (!inArea || !VisibleTarget(enemy, center, transform)) continue;
            enemy.TakeDamage(frost ? catalog.spiritFrostDamage : catalog.spiritStrikeDamage, netId);
            if (frost) enemy.GetComponent<RelativeMovement>()?.ApplySlow(.5f, 2);
            else enemy.GetComponent<RelativeMovement>()?.ServerAddExternalForce(forward * 8 + Vector3.up * 3);
        }
        RpcSpiritStrike(frost, now);
        if (frost) RpcBurst(center, new Color(.3f, .8f, 1), 2, SpellHitKind.Snow);
        else
            for (int i = 1; i <= 3; i++) RpcBurst(center + forward * (i * 2.5f), new Color(1, .3f, .05f), 1, SpellHitKind.Fire);
    }
    public static bool VisibleTarget(Health target, Vector3 point, Transform source)
    {
        foreach (var collider in target.GetComponentsInChildren<Collider>())
            if (collider.enabled && collider.gameObject.activeInHierarchy && ProjectileContact.IsDamageCollider(collider, target) &&
                SpellAreaVisibility.CanReach(point, collider, source)) return true;
        return false;
    }
    [ClientRpc] void RpcSpiritStrike(bool rightToLeft, double startedAt)
        => presentation?.GetComponentInChildren<UltimateSpiritAnimator>(true)?.Strike(rightToLeft, startedAt);

    [Server] bool TryCreateWorld(UltimateDefinition definition, Vector3 point, Ray ray)
    {
        Vector3 forward = Vector3.ProjectOnPlane(ray.direction, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = transform.forward;
        Vector3 position = point + Vector3.up * .06f;
        if (definition.kind == UltimateKind.EarthDepths)
        {
            if (!TryIslandGround(out point)) { LastFailure = "No clear ground below the caster."; return false; }
            position = point;
        }
        if (definition.kind == UltimateKind.GlacierRam)
        {
            position = transform.position + forward * 2.5f + Vector3.up * .2f;
            // Starting overlap is rejected; the sphere must not appear beyond nearby cover.
            if (Trace(new Ray(transform.position + Vector3.up * .6f, forward), 2.6f, false, out _, out _, out _)) return false;
        }
        var root = Instantiate(definition.worldPrefab, position, Quaternion.LookRotation(forward));
        var candidate = root.GetComponent<UltimateWorldEffect>();
        candidate.Initialize(this, catalog, definition.kind, definition.duration);
        if (!candidate.PlacementClear()) { LastFailure = candidate.PlacementFailure; Destroy(root); return false; }
        world = candidate;
        NetworkServer.Spawn(root);
        worldNetId = candidate.netId;
        return true;
    }
}
