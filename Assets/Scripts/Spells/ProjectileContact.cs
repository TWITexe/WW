using UnityEngine;

public static class ProjectileContact
{
    public static bool CanHit(Collider other,Transform source,uint owner)
    {
        if(other.transform.IsChildOf(source)||other.gameObject.layer==2)return false;
        var tactical=other.GetComponentInParent<TacticalEffect>();
        if(tactical!=null)return tactical.CanBeHit(owner);
        var health=other.GetComponentInParent<Health>();
        if(health!=null)return health.netId!=owner&&!health.IsDead;
        return !other.isTrigger&&other.GetComponentInParent<ElementalEffect>()==null&&
            other.GetComponentInParent<FireballProjectile>()==null&&other.GetComponentInParent<WindFlowProjectile>()==null;
    }
    public static bool Sweep(Transform source,uint owner,Vector3 previous,out Collider target)
    {
        var collider=source.GetComponent<Collider>();
        float radius=collider!=null?Mathf.Min(collider.bounds.extents.x,collider.bounds.extents.y,collider.bounds.extents.z):.2f;
        radius=Mathf.Max(.02f,radius);
        var overlaps=Physics.OverlapSphere(previous,radius,~(1<<2),QueryTriggerInteraction.Collide);
        System.Array.Sort(overlaps,(a,b)=>(a.bounds.ClosestPoint(previous)-previous).sqrMagnitude.CompareTo((b.bounds.ClosestPoint(previous)-previous).sqrMagnitude));
        foreach(var other in overlaps)if(CanHit(other,source,owner)){target=other;return true;}
        Vector3 delta=source.position-previous;
        if(delta.sqrMagnitude>.000001f)
        {
            var hits=Physics.SphereCastAll(previous,radius,delta.normalized,delta.magnitude,~(1<<2),QueryTriggerInteraction.Collide);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits)if(CanHit(hit.collider,source,owner)){source.position=hit.point;target=hit.collider;return true;}
        }
        target=null;return false;
    }
}
