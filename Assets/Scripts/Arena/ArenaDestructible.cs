using System.Collections.Generic;
using Mirror;
using UnityEngine;

// сервер хранит состояние укрытия; поздний клиент сразу видит уже разрушенные секции.
[RequireComponent(typeof(NetworkIdentity))]
public class ArenaDestructible : NetworkBehaviour
{
    [Min(1)] public int durability = 45;
    public Color debrisColor = new Color(.42f, .39f, .31f);
    [Range(3, 8)] public int fragmentCount = 6;
    [SyncVar] private Vector3 impactOrigin;
    [SyncVar] private float impactForce;
    [SyncVar(hook = nameof(OnBrokenChanged))] private bool broken;
    private bool clientStarted;
    private int remaining;
    private Renderer[] surfaces;
    private Collider[] colliders;
    private Bounds bounds;
    private static readonly HashSet<ArenaDestructible> active = new HashSet<ArenaDestructible>();
    private static readonly List<ArenaDestructible> candidates = new List<ArenaDestructible>();
    public bool Broken => broken;

    private void Awake()
    {
        surfaces = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
        bounds = new Bounds(transform.position, Vector3.zero);
        if (surfaces.Length > 0) bounds = surfaces[0].bounds;
        foreach (var surface in surfaces) bounds.Encapsulate(surface.bounds);
    }

    public override void OnStartServer() { remaining = durability; active.Add(this); }
    public override void OnStopServer() => active.Remove(this);
    public override void OnStartClient() { clientStarted = true; if (broken) HideIntact(); }

    // прямой контакт не требует повторного луча: столкновение уже подтверждено снарядом.
    public static void Hit(Collider target, Vector3 origin, int damage, float force = 7)
    {
        if (!NetworkServer.active || target == null) return;
        target.GetComponent<UltimateMirror>()?.ReceiveDamage(damage);
        target.GetComponentInParent<ArenaDestructible>()?.Damage(origin, damage, force);
    }

    // сначала собираем видимые цели, затем ломаем: первый пролом не пропускает этот же взрыв сквозь стену.
    public static void Blast(Vector3 origin, float radius, int damage, Transform source, float force = 8)
    {
        if (!NetworkServer.active || damage <= 0 || radius <= 0) return;
        UltimateWorldEffect.BlastMirrors(origin, radius, damage, source);
        candidates.Clear();
        foreach (var item in active)
        {
            if (item == null || item.broken || item.bounds.SqrDistance(origin) > radius * radius) continue;
            if (item.VisibleFrom(origin, source)) candidates.Add(item);
        }
        foreach (var item in candidates) item.Damage(origin, damage, force);
        candidates.Clear();
    }

    private bool VisibleFrom(Vector3 origin, Transform source)
    {
        Vector3 delta = bounds.center - origin;
        if (delta.sqrMagnitude < .01f) return true;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || (source != null && hit.transform.IsChildOf(source))) continue;
            if (hit.collider.GetComponentInParent<Health>() != null) continue;
            return false;
        }
        return true;
    }

    [Server] public void Damage(Vector3 origin, int damage, float force)
    {
        if (broken) return;
        remaining -= Mathf.Max(0, damage);
        if (remaining > 0) return;
        impactOrigin = origin;
        impactForce = Mathf.Clamp(force, 3, 12);
        broken = true;
        HideIntact();
        // хост не ждёт доставки собственного состояния по сети.
        if (NetworkClient.active) ArenaDebris.Emit(bounds, debrisColor, fragmentCount, origin, impactForce);
    }

    private void OnBrokenChanged(bool previous, bool value)
    {
        if (!value) return;
        HideIntact();
        if (!isServer && clientStarted) ArenaDebris.Emit(bounds, debrisColor, fragmentCount, impactOrigin, impactForce);
    }

    private void HideIntact()
    {
        foreach (var surface in surfaces) if (surface != null) surface.enabled = false;
        foreach (var collider in colliders) if (collider != null) collider.enabled = false;
    }
}
