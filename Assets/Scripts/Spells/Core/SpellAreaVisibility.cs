using UnityEngine;

// проверяем укрытие отдельно от поиска целей; персонажи и триггеры не становятся стенами.
public static class SpellAreaVisibility
{
    private static readonly RaycastHit[] hits = new RaycastHit[32];

    public static bool CanReach(Vector3 origin, Collider target, Transform effect)
    {
        Bounds bounds = target.bounds;
        // открытая верхняя часть тела тоже считается доступной из-за низкого укрытия.
        return ClearPath(origin, bounds.center, effect) ||
            ClearPath(origin, bounds.center + Vector3.up * bounds.extents.y * .75f, effect);
    }

    private static bool ClearPath(Vector3 origin, Vector3 target, Transform effect)
    {
        // обратный луч замечает стену даже тогда, когда центр области оказался внутри её коллайдера.
        return ClearSegment(origin, target, effect) && ClearSegment(target, origin, effect);
    }

    private static bool ClearSegment(Vector3 origin, Vector3 target, Transform effect)
    {
        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance < .001f) return true;
        int count = Physics.RaycastNonAlloc(origin, delta / distance, hits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        // при переполнении проверяем все поверхности, чтобы сложная сцена не пропускала урон сквозь стены.
        RaycastHit[] results = count == hits.Length
            ? Physics.RaycastAll(origin, delta / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : hits;
        if (results != hits) count = results.Length;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = results[i].collider;
            if (effect != null && obstacle.transform.IsChildOf(effect)) continue;
            if (obstacle.GetComponentInParent<Health>() != null) continue;
            return false;
        }
        return true;
    }
}
