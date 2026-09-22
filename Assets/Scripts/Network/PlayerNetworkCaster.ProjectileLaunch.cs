using UnityEngine;

public partial class PlayerNetworkCaster
{
    // выпускаем снаряд из прежней точки перед лицом; луч камеры задаёт только цель полёта.
    private bool TryProjectileLaunch(GameObject prefab, Ray ray, Vector3 fallbackDirection,
        out Vector3 position, out Vector3 direction)
    {
        position = ShotOrigin();
        direction = fallbackDirection.normalized;
        if (!LargeProjectile(prefab)) return true;

        Vector3 halfSize = ProjectileHalfSize(prefab);
        Vector3 anchor = transform.position + Vector3.up * .6f;
        Vector3 target = FirstAimHit(ray, 100, out var aimHit) ? aimHit.point : ray.GetPoint(100);
        var shape = prefab.GetComponent<Collider>();
        Vector3 center = shape is SphereCollider sphere ? sphere.center :
            shape is CapsuleCollider capsule ? capsule.center : Vector3.zero;
        center = Vector3.Scale(center, prefab.transform.localScale);
        Vector3 facePosition = position;

        // сначала пробуем исходную точку; при пересечении пола немного поднимаем крупный снаряд возле лица.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 candidate = facePosition + Vector3.up * (attempt * .25f);
            Vector3 aimDirection = target - candidate;
            if (aimDirection.sqrMagnitude < .0001f) continue;
            Quaternion rotation = Quaternion.LookRotation(aimDirection);
            if (LaunchPathBlocked(anchor, candidate) || LaunchPathBlocked(facePosition, candidate)) continue;
            bool blocked = false;
            foreach (var obstacle in Physics.OverlapBox(candidate + rotation * center, halfSize + Vector3.one * .03f,
                rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                if (BlocksProjectileLaunch(obstacle)) { blocked = true; break; }
            if (blocked) continue;
            position = candidate;
            direction = aimDirection.normalized;
            return true;
        }
        // за укрытием снаряд не создаём: отказ не расходует кулдаун и не переносит атаку сквозь стену.
        return false;
    }

    private bool LaunchPathBlocked(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .000001f) return false;
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            if (BlocksProjectileLaunch(hit.collider)) return true;
        return false;
    }

    private bool BlocksProjectileLaunch(Collider obstacle) =>
        !obstacle.transform.IsChildOf(transform.root) && obstacle.GetComponentInParent<Health>() == null &&
        ProjectileContact.CanHit(obstacle, transform, netId);

    private Ray ProjectileAimRay(Vector3 direction) => resolvingCommand ? commandAimRay :
        emittingChannelDrop ? channelRay : new Ray(ShotOrigin(), direction.normalized);

    // используем сериализованную форму, поскольку bounds префаба вне сцены не описывает рабочий коллайдер.
    public static Vector3 ProjectileHalfSize(GameObject prefab)
    {
        var collider = prefab.GetComponent<Collider>();
        Vector3 scale = prefab.transform.localScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        if (collider is SphereCollider sphere)
            return Vector3.one * sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        if (collider is CapsuleCollider capsule)
        {
            float radius = capsule.radius * Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);
            Vector3 size = Vector3.one * radius;
            size[capsule.direction] = Mathf.Max(radius, capsule.height * scale[capsule.direction] * .5f);
            return size;
        }
        return Vector3.one * .2f;
    }

    public static bool LargeProjectile(GameObject prefab)
    {
        Vector3 size = ProjectileHalfSize(prefab);
        return Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= .6f;
    }
}
