using System.Collections.Generic;
using Mirror;
using UnityEngine;

[DefaultExecutionOrder(-150)]
public partial class UltimateWorldEffect : NetworkBehaviour
{
    public UltimateCatalog catalog;
    public UltimateKind kind;
    public UltimateMirror[] mirrors;
    public Transform rotatingVisual;
    public LineRenderer warningRing;
    [SyncVar] public uint ownerId;
    [SyncVar] private double expiresAt, slamAt;
    [SyncVar(hook = nameof(OnMirrorsChanged))] private int intactMirrors;
    [SyncVar] private Vector3 serverPosition;
    private PlayerUltimate owner;
    private readonly int[] mirrorHP = new int[6];
    private readonly Dictionary<Health, double> contactTimes = new Dictionary<Health, double>();
    private readonly HashSet<Health> touched = new HashSet<Health>();
    private readonly HashSet<Health> gravityTargets = new HashSet<Health>();
    private readonly List<Health> gravityRemove = new List<Health>();
    private Ray aim;
    private double aimAt, nextTick;
    private float fallSpeed;
    private const float OrbSkin = .03f;
    private bool finished;
    private Vector3 steering, previousPosition, displayedPosition;
    private double steeringAt;
    private bool displayReady;
    private double nextOrbJump;
    private Vector3 lastRollPosition;
    private bool rollReady;
    public Vector3 PresentationPosition => displayReady ? displayedPosition : transform.position;
    private static readonly HashSet<UltimateWorldEffect> serverEffects = new HashSet<UltimateWorldEffect>();
    public double ExpiresAt => expiresAt;
    public int IntactMirrors => intactMirrors;
    public bool Finished => finished;
    public bool SlamPending => slamAt > 0 && !finished;
    public string PlacementFailure { get; private set; }

    public void Initialize(PlayerUltimate source, UltimateCatalog settings, UltimateKind type, float duration)
    {
        owner = source; ownerId = source.netId; catalog = settings; kind = type;
        expiresAt = NetworkTime.time + duration; serverPosition = transform.position;
        islandOrigin = transform.position; islandStarted = NetworkTime.time;
        riseHeight = catalog.islandHeight;
        if (kind == UltimateKind.EarthDepths)
        {
            foreach (var hit in Physics.BoxCastAll(islandOrigin + Vector3.up * 1.1f,
                new Vector3(catalog.islandRadius, .95f, catalog.islandRadius), Vector3.up,
                Quaternion.identity, riseHeight, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Health>() != null) continue;
                riseHeight = Mathf.Min(riseHeight, Mathf.Max(0, hit.distance - .08f));
            }
        }
    }
    public override void OnStartServer()
    {
        serverEffects.Add(this);
        for (int i = 0; i < mirrorHP.Length; i++) mirrorHP[i] = catalog.mirrorHealth;
        nextTick = NetworkTime.time + .5;
    }
    public override void OnStopServer()
    {
        serverEffects.Remove(this);
        ClearGravityTargets();
    }
    public override void OnStartClient() { ApplyMirrorMask(); RefreshMirrorPoses(); }
    void OnMirrorsChanged(int previous, int current) => ApplyMirrorMask();
    void ApplyMirrorMask()
    {
        if (mirrors == null) return;
        for (int i = 0; i < mirrors.Length; i++) if (mirrors[i] != null) mirrors[i].gameObject.SetActive((intactMirrors & (1 << i)) != 0);
    }

    // Validate the actual baked collision volumes, excluding the new effect itself.
    public bool PlacementClear()
    {
        if (kind == UltimateKind.EarthDepths || kind == UltimateKind.MirrorLabyrinth) return true;
        Physics.SyncTransforms();
        foreach (var shape in GetComponentsInChildren<Collider>())
        {
            if (!shape.enabled || (shape.isTrigger && kind != UltimateKind.GlacierRam)) continue;
            var bounds = shape.bounds;
            foreach (var obstacle in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.transform.IsChildOf(transform) || obstacle.isTrigger) continue;
                if (Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out float depth) && depth > .04f)
                { PlacementFailure = shape.name + " overlaps " + obstacle.name + " by " + depth; return false; }
            }
        }
        return true;
    }
    public void SetAim(Ray value, double received) { aim = value; aimAt = received; }
    public void SetSteering(Vector3 value, double received) { steering = Vector3.ClampMagnitude(value, 1); steeringAt = received; }
    void Update()
    {
        if (isClient && !isServer && kind == UltimateKind.GlacierRam)
            transform.position = Vector3.Lerp(transform.position, serverPosition, 1 - Mathf.Exp(-18 * Time.deltaTime));
        if (rotatingVisual != null && kind != UltimateKind.GlacierRam)
            rotatingVisual.Rotate(Vector3.up, Time.deltaTime * 12, Space.Self);
        if (warningRing != null)
        {
            warningRing.startColor = warningRing.endColor = slamAt > 0 ? new Color(1, .25f, .05f) : new Color(.3f, .85f, 1);
            warningRing.widthMultiplier = slamAt > 0 ? .22f : .1f;
        }
        if (!isServer || finished) return;
        ServerTick(NetworkTime.time);
    }
    void LateUpdate()
    {
        if (kind == UltimateKind.MirrorLabyrinth) { RefreshMirrorPoses(); ApplyMirrorMask(); }
        if (kind != UltimateKind.GlacierRam) return;
        float fraction = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        displayedPosition = isServer && displayReady ? Vector3.Lerp(previousPosition, transform.position, fraction) : transform.position;
        displayReady = true;
        if (rotatingVisual != null)
        {
            Vector3 travel = Vector3.ProjectOnPlane(displayedPosition - lastRollPosition, Vector3.up);
            if (rollReady && travel.sqrMagnitude < 9 && travel.sqrMagnitude > .000001f)
            {
                // World-space roll axis follows the actual displacement, including reversals and strafing.
                Vector3 axis = Vector3.Cross(Vector3.up, travel.normalized);
                rotatingVisual.Rotate(axis, travel.magnitude / catalog.orbRadius * Mathf.Rad2Deg, Space.World);
            }
            rotatingVisual.position = displayedPosition;
        }
        lastRollPosition = displayedPosition; rollReady = true;
    }
    void FixedUpdate()
    {
        previousPosition = transform.position;
        if (!finished && kind == UltimateKind.EarthDepths) StepIsland(NetworkTime.time);
        if (isServer && !finished && kind == UltimateKind.GlacierRam) StepOrb(Time.fixedDeltaTime);
    }
    [Server] public void ServerTick(double now)
    {
        if (owner == null || owner.GetComponent<Health>().IsDead || !NetManager.CombatAllowed) { ServerFinish(false); return; }
        if (kind == UltimateKind.GravityInversion)
        {
            if (slamAt > 0 && now >= slamAt) { Slam(); ServerFinish(false); return; }
            LiftTargets();
            if (now >= expiresAt && slamAt <= 0) RequestSlam();
            return;
        }
        if (kind == UltimateKind.EarthDepths)
        {
            if (collapseAt > 0) return;
            while (nextTick <= now && nextTick <= expiresAt + .00001)
            {
                nextTick += .5;
                ApplyLava();
            }
            if (now >= expiresAt) RequestCollapse();
            return;
        }
        if (now >= expiresAt) ServerFinish(true);
    }
    bool Enemy(Health target) => target != null && !target.IsDead && owner != null && !PlayerUltimate.Allied(owner.GetComponent<Health>(), target);

    [Server] void ApplyLava()
    {
        foreach (var target in Health.ServerInstances)
        {
            if (!Enemy(target)) continue;
            Vector3 local = transform.InverseTransformPoint(target.transform.position);
            float radius = new Vector2(local.x, local.z).magnitude;
            bool channel = radius <= catalog.islandRadius;
            if (channel && local.y >= -.5f && local.y <= 1.8f && PlayerUltimate.VisibleTarget(target, transform.position + Vector3.up * .3f, transform))
                target.TakeDamage(Mathf.RoundToInt(catalog.lavaDamagePerSecond * .5f), ownerId);
        }
    }
    bool InGravity(Health target)
    {
        if (target == null || target.IsDead || target.GetComponent<PlayerUltimate>()?.IsMeteor == true) return false;
        Vector3 delta = target.transform.position - transform.position;
        return new Vector2(delta.x, delta.z).sqrMagnitude <= catalog.gravityRadius * catalog.gravityRadius &&
            delta.y >= -.5f && delta.y <= catalog.gravityHeight + 5 &&
            PlayerUltimate.VisibleTarget(target, transform.position + Vector3.up * .5f, transform);
    }
    [Server] void LiftTargets()
    {
        gravityRemove.Clear();
        foreach (var target in gravityTargets) if (!InGravity(target)) gravityRemove.Add(target);
        foreach (var target in gravityRemove) gravityTargets.Remove(target);
        foreach (var target in Health.ServerInstances)
        {
            if (!InGravity(target)) continue;
            gravityTargets.Add(target);
            target.GetComponent<RelativeMovement>()?.ServerGravityField(transform.position.y + catalog.gravityHeight);
        }
    }
    [Server] public bool RequestSlam()
    {
        if (finished || kind != UltimateKind.GravityInversion || slamAt > 0) return false;
        slamAt = NetworkTime.time + catalog.slamWarning;
        expiresAt = slamAt + .05;
        return true;
    }
    [Server] void Slam()
    {
        foreach (var target in Health.ServerInstances)
            if (InGravity(target)) target.GetComponent<RelativeMovement>()?.ServerSlam(catalog.landingDamage, ownerId);
        gravityTargets.Clear();
    }
    void ClearGravityTargets() { gravityTargets.Clear(); gravityRemove.Clear(); }

    [Server] public void StepOrb(float dt)
    {
        if (owner == null || finished) return;
        Vector3 position = transform.position;
        Vector3 direction = NetworkTime.time - steeringAt <= .25 ? steering : Vector3.zero;
        Vector3 step = direction * catalog.orbSpeed * dt;
        float nearest = step.magnitude;
        // A full-radius sweep tangent to the floor can report an initial overlap with
        // a synthetic horizontal normal. Leave a skin so the floor is not a wall.
        float sweepRadius = Mathf.Max(.01f, catalog.orbRadius - OrbSkin);
        if (step.sqrMagnitude > .000001f)
            foreach (var hit in Physics.SphereCastAll(position, sweepRadius, direction, nearest,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Health>() != null || hit.normal.y > .65f) continue;
                // Expand the stopping margin back to the visible sphere's radius.
                float stop = Mathf.Max(0, hit.distance - OrbSkin);
                nearest = Mathf.Min(nearest, stop);
            }
        step = direction * nearest;
        // Wall contact stops translation; steering can turn the sphere away on the next tick.
        Vector3 next = position + step;
        fallSpeed = Mathf.Max(-22, fallSpeed - 14 * dt);
        bool supported = false;
        float probeRadius = catalog.orbRadius * .9f;
        Vector3 probeOrigin = next + Vector3.up * .4f;
        float probeDistance = .4f + (catalog.orbRadius - probeRadius + OrbSkin) / .65f + Mathf.Max(0, -fallSpeed * dt);
        var supports = fallSpeed <= 0 ? Physics.SphereCastAll(probeOrigin, probeRadius,
            Vector3.down, probeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : System.Array.Empty<RaycastHit>();
        System.Array.Sort(supports, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var floor in supports)
        {
            // A probe starting inside a low ceiling reports a synthetic upward normal at distance zero.
            // Such an overlap is not a floor and must never lift the sphere through the ceiling.
            if (floor.distance <= .001f || floor.collider.transform.IsChildOf(transform) || floor.collider.GetComponentInParent<Health>() != null || floor.normal.y < .65f) continue;
            // Use the probe's center and surface normal: contact-point Y alone
            // sinks the full sphere into slopes and recreates an initial overlap.
            next.y = probeOrigin.y - floor.distance + (catalog.orbRadius - probeRadius + OrbSkin) / floor.normal.y;
            fallSpeed = 0; supported = true; break;
        }
        if (!supported)
        {
            float rise = fallSpeed * dt;
            if (rise > 0)
            {
                foreach (var ceiling in Physics.SphereCastAll(next, sweepRadius, Vector3.up, rise + OrbSkin,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (ceiling.collider.transform.IsChildOf(transform) || ceiling.collider.GetComponentInParent<Health>() != null) continue;
                    rise = Mathf.Min(rise, Mathf.Max(0, ceiling.distance - OrbSkin)); fallSpeed = 0;
                }
            }
            next.y += rise;
        }
        Vector3 displacement = next - position;
        touched.Clear();
        foreach (var collider in Physics.OverlapSphere(position, catalog.orbRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            HitOrbTarget(collider, direction);
        if (displacement.sqrMagnitude > .000001f)
            foreach (var hit in Physics.SphereCastAll(position, catalog.orbRadius, displacement.normalized, displacement.magnitude,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) HitOrbTarget(hit.collider, direction);
        transform.position = serverPosition = next;
        if ((next - owner.transform.position).sqrMagnitude > 40 * 40) ServerFinish(true);
    }
    [Server] public bool TryJumpOrb()
    {
        if (kind != UltimateKind.GlacierRam || finished || NetworkTime.time >= expiresAt ||
            owner == null || !owner.Active || owner.GetComponent<Health>().IsDead ||
            owner.GetComponent<RelativeMovement>().IsStunned || !NetManager.CombatAllowed ||
            fallSpeed > .01f || NetworkTime.time < nextOrbJump) return false;
        // A fresh support probe prevents mid-air jumps, including after walking off an edge.
        float radius = catalog.orbRadius * .9f;
        float distance = .12f + catalog.orbRadius - radius + OrbSkin;
        foreach (var hit in Physics.SphereCastAll(transform.position, radius, Vector3.down, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= .001f || hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Health>() != null || hit.normal.y < .65f) continue;
            fallSpeed = catalog.orbJumpSpeed;
            nextOrbJump = NetworkTime.time + .2;
            return true;
        }
        return false;
    }
    void HitOrbTarget(Collider collider, Vector3 direction)
    {
        var target = collider.GetComponentInParent<Health>();
        if (!Enemy(target) || !ProjectileContact.IsDamageCollider(collider, target) || !touched.Add(target)) return;
        if (contactTimes.TryGetValue(target, out double last) && NetworkTime.time < last + 1) return;
        if (!PlayerUltimate.VisibleTarget(target, transform.position, transform)) return;
        contactTimes[target] = NetworkTime.time;
        target.TakeDamage(catalog.orbContactDamage, ownerId);
        target.GetComponent<RelativeMovement>()?.ServerAddExternalForce(direction * 18 + Vector3.up * 4);
    }

    [Server] public void DamageMirror(int index, int damage)
    {
        if (finished || kind != UltimateKind.MirrorLabyrinth || index < 0 || index >= mirrorHP.Length || (intactMirrors & (1 << index)) == 0) return;
        mirrorHP[index] -= Mathf.Max(0, damage);
        if (mirrorHP[index] > 0) return;
        intactMirrors &= ~(1 << index);
        if (owner != null && mirrors != null && index < mirrors.Length)
            owner.RpcBurst(mirrors[index].transform.position, Color.cyan, 1, SpellHitKind.Snow);
        ApplyMirrorMask();
    }
    public static void BlastMirrors(Vector3 point, float radius, int damage, Transform source)
    {
        foreach (var effect in serverEffects)
        {
            if (effect.kind != UltimateKind.MirrorLabyrinth || effect.mirrors == null) continue;
            foreach (var mirror in effect.mirrors)
            {
                if (mirror == null || !mirror.gameObject.activeInHierarchy) continue;
                var collider = mirror.GetComponent<Collider>();
                if (collider.bounds.SqrDistance(point) <= radius * radius && SpellAreaVisibility.CanReach(point, collider, source)) mirror.ReceiveDamage(damage);
            }
        }
    }
    [Server] public void ServerFinish(bool detonate)
    {
        if (finished) return;
        finished = true;
        if (detonate && (kind == UltimateKind.GlacierRam || kind == UltimateKind.EarthDepths))
        {
            float radius = kind == UltimateKind.GlacierRam ? catalog.orbBlastRadius : 6;
            int damage = kind == UltimateKind.GlacierRam ? catalog.orbBlastDamage : catalog.collapseDamage;
            foreach (var target in Health.ServerInstances)
            {
                if (!Enemy(target) || (target.transform.position - transform.position).sqrMagnitude > radius * radius ||
                    !PlayerUltimate.VisibleTarget(target, transform.position + Vector3.up * .2f, transform)) continue;
                target.TakeDamage(damage, ownerId);
                if (kind == UltimateKind.GlacierRam && !target.IsDead) target.GetComponent<RelativeMovement>()?.ServerStun(catalog.orbStun);
            }
            ArenaDestructible.Blast(transform.position, radius, damage, transform);
            owner?.RpcBurst(transform.position, kind == UltimateKind.GlacierRam ? Color.cyan : new Color(1, .3f, .02f),
                2.5f, kind == UltimateKind.GlacierRam ? SpellHitKind.Snow : SpellHitKind.Stone);
        }
        owner?.WorldFinished(this);
        NetworkServer.Destroy(gameObject);
    }
}
