using Mirror;
using UnityEngine;

public partial class PlayerUltimate
{
    [Command] void CmdSteerOrb(Vector3 direction)
    {
        if (!CanAct || ControlledOrb == null || !Finite(direction) || direction.sqrMagnitude > 1.01f) return;
        world.SetSteering(Vector3.ProjectOnPlane(direction, Vector3.up), NetworkTime.time);
    }
    [Command] void CmdJumpOrb()
    {
        if (CanAct && ControlledOrb != null) world.TryJumpOrb();
    }

    [Command] void CmdPlaceMirror(Vector3 origin, Vector3 direction, float yaw)
        => ServerPlaceMirror(new Ray(origin, direction), yaw);

    [Server] public bool ServerPlaceMirror(Ray ray, float yaw)
    {
        if (!CanAct || Definition == null || Definition.kind != UltimateKind.MirrorLabyrinth ||
            !ValidAim(ray.origin, ray.direction) || float.IsNaN(yaw) || float.IsInfinity(yaw) ||
            NetworkTime.time < nextCommandAt || (active && (charges >= 6 || NetworkTime.time >= activeUntil))) return false;
        if (!caster.TryGroundTarget(ray, out var point) || !UltimateWorldEffect.MirrorPlacementClear(point, yaw)) return false;
        nextCommandAt = NetworkTime.time + .15;
        if (!active && !ServerActivate(ray)) return false;
        if (world == null || !world.PlaceMirror(point, yaw)) return false;
        charges++;
        // Allow deliberate placement; after the sixth mirror all six share a full combat window.
        if (charges == 1) activeUntil = NetworkTime.time + UltimateCatalog.MirrorPlacementDuration;
        else if (charges == 6) activeUntil = NetworkTime.time + Definition.duration;
        world.SetExpiry(activeUntil);
        return true;
    }

    bool TryIslandGround(out Vector3 point)
    {
        point = transform.position;
        var hits = Physics.RaycastAll(transform.position + Vector3.up * .2f, Vector3.down, 3,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<Health>() != null || hit.normal.y < .85f) continue;
            point = hit.point;
            // An island cannot emerge through nearby walls or low ceilings.
            foreach (var obstacle in Physics.OverlapBox(point + Vector3.up * 1.1f,
                new Vector3(catalog.islandRadius, 1, catalog.islandRadius), Quaternion.identity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (obstacle != hit.collider && obstacle.GetComponentInParent<Health>() == null) return false;
            return true;
        }
        return false;
    }
}
