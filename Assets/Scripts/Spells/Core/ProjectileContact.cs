using UnityEngine;

// задаёт общие правила столкновений и непрерывную проверку пути для всех основных снарядов.
public static class ProjectileContact
{
    private const float SurfaceOffset = .15f;

    // исключаем владельца, мёртвые цели, декоративный слой и посторонние эффекты.
    public static bool CanHit(Collider other, Transform source, uint owner)
    {
        if (other.transform.IsChildOf(source) || other.gameObject.layer == 2) return false;
        var tactical = other.GetComponentInParent<TacticalEffect>();
        if (tactical != null) return tactical.CanBeHit(owner);
        var health = other.GetComponentInParent<Health>();
        if (health != null) return health.netId != owner && !health.IsDead && IsDamageCollider(other, health);
        var advanced = other.GetComponentInParent<AdvancedSpellEffect>();
        if (advanced != null) return advanced.definition != null && advanced.definition.kind == AdvancedSpellKind.IceBridge;
        return !other.isTrigger && other.GetComponentInParent<ElementalEffect>() == null &&
            other.GetComponentInParent<FireballProjectile>() == null && other.GetComponentInParent<WindFlowProjectile>() == null;
    }

    // прочие цели сохраняют свои коллайдеры; у мага урон принимает только анимированное тело.
    public static bool IsDamageCollider(Collider collider, Health health)
    {
        var appearance = health.GetComponent<WizardAppearance>();
        return appearance == null || !appearance.HasAnimatedHitboxes || appearance.IsDamageCollider(collider);
    }

    // возвращаем точку снаружи поверхности: центр быстрого снаряда к моменту триггера уже может быть внутри стены.
    public static Vector3 ImpactPosition(Collider target, Transform source, Vector3 previous)
    {
        Vector3 travel = source.position - previous;
        if (travel.sqrMagnitude > .000001f &&
            target.Raycast(new Ray(previous, travel.normalized), out var hit, travel.magnitude + SurfaceOffset))
            return hit.point + hit.normal * SurfaceOffset;
        Vector3 point = target.ClosestPoint(previous);
        Vector3 outward = previous - point;
        if (outward.sqrMagnitude > .000001f)
            return point + outward.normalized * SurfaceOffset;
        // если снаряд родился в перекрытии, ищем входную поверхность со стороны, противоположной полёту.
        Vector3 direction = travel.sqrMagnitude > .000001f ? travel.normalized : source.forward;
        float distance = target.bounds.extents.magnitude * 2 + Vector3.Distance(source.position, target.bounds.center) + 1;
        if (target.Raycast(new Ray(source.position - direction * distance, direction), out hit, distance * 2))
            return hit.point + hit.normal * SurfaceOffset;
        return source.position - direction * SurfaceOffset;
    }

    // проверяем начальное перекрытие и весь путь; отдельно возвращаем точку графики на видимой стороне поверхности.
    public static bool Sweep(Transform source, uint owner, Vector3 previous, out Collider target, out Vector3 impactPosition, float radiusOverride = -1)
    {
        var collider = source.GetComponent<Collider>();
        float radius = collider != null ? Mathf.Min(collider.bounds.extents.x, collider.bounds.extents.y, collider.bounds.extents.z) : .2f;
        radius = Mathf.Max(.02f, radius);
        if (radiusOverride > 0) radius = radiusOverride;
        Vector3 tipA=previous,tipB=previous;
        bool capsule=collider is CapsuleCollider && radiusOverride<=0;
        if(capsule)
        {
            var shape=(CapsuleCollider)collider;
            Vector3 axis=shape.direction==0?Vector3.right:shape.direction==1?Vector3.up:Vector3.forward;
            Vector3 scale=source.lossyScale;
            float lengthScale=Mathf.Abs(scale[shape.direction]);
            radius=shape.radius*Mathf.Max(Mathf.Abs(scale[(shape.direction+1)%3]),Mathf.Abs(scale[(shape.direction+2)%3]));
            Vector3 center=previous+source.TransformPoint(shape.center)-source.position;
            Vector3 half=source.TransformDirection(axis)*Mathf.Max(0,shape.height*lengthScale*.5f-radius);
            tipA=center+half;tipB=center-half;
        }
        var overlaps = capsule ? Physics.OverlapCapsule(tipA,tipB,radius,~(1<<2),QueryTriggerInteraction.Collide) :
            Physics.OverlapSphere(previous, radius, ~(1 << 2), QueryTriggerInteraction.Collide);
        System.Array.Sort(overlaps, (a, b) => (a.bounds.ClosestPoint(previous) - previous).sqrMagnitude.CompareTo((b.bounds.ClosestPoint(previous) - previous).sqrMagnitude));
        foreach (var other in overlaps)
        {
            if (!CanHit(other, source, owner)) continue;
            target = other;
            impactPosition = ImpactPosition(other, source, previous);
            return true;
        }
        Vector3 delta = source.position - previous;
        if (delta.sqrMagnitude > .000001f)
        {
            var hits = capsule ? Physics.CapsuleCastAll(tipA,tipB,radius,delta.normalized,delta.magnitude,~(1<<2),QueryTriggerInteraction.Collide) :
                Physics.SphereCastAll(previous, radius, delta.normalized, delta.magnitude, ~(1 << 2), QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (!CanHit(hit.collider, source, owner)) continue;
                source.position = hit.point;
                target = hit.collider;
                impactPosition = hit.point + hit.normal * SurfaceOffset;
                return true;
            }
        }
        target = null;
        impactPosition = source.position;
        return false;
    }
}
