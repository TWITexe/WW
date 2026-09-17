using Mirror;
using System.Collections.Generic;
using UnityEngine;

public class Trap : NetworkBehaviour
{
    [SerializeField] private int damage = 10;
    [SerializeField] private float damageCooldown = 1f;
    private Collider[] volumes;
    private readonly Dictionary<Health, double> nextDamage = new Dictionary<Health, double>();
    private readonly HashSet<Health> occupants = new HashSet<Health>();
    private readonly List<Health> expired = new List<Health>();
    private void Awake() => volumes = GetComponentsInChildren<Collider>();
    private void FixedUpdate()
    {
        if (!isServer) return;
        occupants.Clear();
        // A moving CharacterController is not reliably returned by shallow floor
        // overlap queries. Use its server transform and capsule dimensions directly.
        foreach (var health in Health.ServerInstances)
        {
            if (health == null || health.IsDead || !health.gameObject.activeInHierarchy) continue;
            var controller = health.GetComponent<CharacterController>();
            if (controller == null) continue;
            foreach (var volume in volumes)
                if (volume is BoxCollider box && box.enabled && box.isTrigger && box.gameObject.activeInHierarchy && CapsuleOverlapsBox(controller, box))
                { occupants.Add(health); break; }
        }
        foreach (var volume in volumes)
        {
            if (volume == null || !volume.enabled || !volume.gameObject.activeInHierarchy || !volume.isTrigger) continue;
            var bounds = volume.bounds;
            // Query the actual oriented box. CharacterController is a query shape;
            // a second ComputePenetration check can reject its shallow foot overlap.
            Collider[] overlaps;
            if (volume is BoxCollider box)
            {
                Vector3 scale = box.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                overlaps = Physics.OverlapBox(box.transform.TransformPoint(box.center),
                    Vector3.Scale(box.size, scale) * .5f, box.transform.rotation, ~0, QueryTriggerInteraction.Collide);
            }
            else overlaps = Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            foreach (var other in overlaps)
            {
                var health = other.GetComponentInParent<Health>();
                if (health == null || health.IsDead || !other.enabled) continue;
                // Characters were checked from their current server pose above;
                // never re-add one from a stale physics pose after teleporting.
                if (volume is BoxCollider && health.GetComponent<CharacterController>() != null) continue;
                if (volume is BoxCollider || Physics.ComputePenetration(volume, volume.transform.position, volume.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out _))
                    occupants.Add(health);
            }
        }
        double now = NetworkTime.time;
        foreach (var health in occupants)
            if (!nextDamage.TryGetValue(health, out double next) || now >= next)
            {
                nextDamage[health] = now + Mathf.Max(.05f, damageCooldown);
                health.TakeDamage(damage);
            }
        expired.Clear();
        foreach (var entry in nextDamage)
            if (entry.Key == null || entry.Key.IsDead || !occupants.Contains(entry.Key)) expired.Add(entry.Key);
        foreach (var health in expired) nextDamage.Remove(health);
    }
    private static bool CapsuleOverlapsBox(CharacterController controller, BoxCollider box)
    {
        Vector3 playerScale=controller.transform.lossyScale;
        float radius=controller.radius*Mathf.Max(Mathf.Abs(playerScale.x),Mathf.Abs(playerScale.z));
        float halfSegment=Mathf.Max(0,controller.height*Mathf.Abs(playerScale.y)*.5f-radius);
        Vector3 center=controller.transform.TransformPoint(controller.center);
        Vector3 axis=controller.transform.up*halfSegment;
        Quaternion toBox=Quaternion.Inverse(box.transform.rotation);
        Vector3 boxCenter=box.transform.TransformPoint(box.center);
        Vector3 a=toBox*(center-axis-boxCenter), b=toBox*(center+axis-boxCenter);
        Vector3 scale=box.transform.lossyScale;
        Vector3 extents=Vector3.Scale(box.size,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)))*.5f;
        // Squared distance from a segment to a box is convex. This also handles
        // tilted traps without treating their larger world AABB as damaging space.
        float lo=0,hi=1;
        for(int i=0;i<24;i++)
        {
            float left=(lo*2+hi)/3,right=(lo+hi*2)/3;
            if(DistanceToBox(Vector3.Lerp(a,b,left),extents)<DistanceToBox(Vector3.Lerp(a,b,right),extents))hi=right;
            else lo=left;
        }
        float distance=Mathf.Min(DistanceToBox(a,extents),DistanceToBox(b,extents),DistanceToBox(Vector3.Lerp(a,b,(lo+hi)*.5f),extents));
        return distance<=radius*radius;
    }
    private static float DistanceToBox(Vector3 point,Vector3 extents)
    {
        Vector3 outside=new Vector3(Mathf.Max(0,Mathf.Abs(point.x)-extents.x),Mathf.Max(0,Mathf.Abs(point.y)-extents.y),Mathf.Max(0,Mathf.Abs(point.z)-extents.z));
        return outside.sqrMagnitude;
    }
    private void OnDisable() { nextDamage.Clear(); occupants.Clear(); }
}
