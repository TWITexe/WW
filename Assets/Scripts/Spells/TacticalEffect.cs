using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class TacticalEffect : NetworkBehaviour
{
    public TacticalSpell definition;
    [SyncVar] public uint ownerId;
    [SyncVar] private double bornAt;
    private double expiresAt, nextTick;
    private bool spent;
    public bool Armed => NetworkTime.time >= bornAt + 1;
    public bool CanBeHit(uint projectileOwner) => !spent && definition != null &&
        (definition.kind == TacticalKind.StoneWall || (projectileOwner != ownerId &&
        (definition.kind == TacticalKind.IceMirror || definition.kind == TacticalKind.SnowDecoy)));

    public override void OnStartServer()
    {
        bornAt = NetworkTime.time;
        expiresAt = bornAt + definition.duration;
    }
    private float nextAppearanceRefresh;
    private bool collapsed;
    public override void OnStartClient() => RefreshDecoyAppearance();
    private void LateUpdate()
    {
        if(!isClient||Time.time<nextAppearanceRefresh)return;
        nextAppearanceRefresh=Time.time+.2f;
        RefreshDecoyAppearance();
    }
    private void RefreshDecoyAppearance()
    {
        if(definition==null||definition.kind!=TacticalKind.SnowDecoy||!NetworkClient.spawned.TryGetValue(ownerId,out var owner))return;
        var source=owner.GetComponentInChildren<WizardAppearance>();
        if(source==null||source.visualRoot==null)return;
        var originals=source.visualRoot.GetComponentsInChildren<Renderer>(true);
        foreach(var target in GetComponentsInChildren<Renderer>(true))
            foreach(var original in originals)
                if(target.name==original.name){DecoyAppearance.Copy(original,target);break;}
    }
    [ClientRpc] private void RpcCollapseDecoy() => CollapseDecoy();
    private void CollapseDecoy()
    {
        if(collapsed)return;
        collapsed=true;
        RefreshDecoyAppearance();
        DecoyAppearance.Collapse(transform.Find("Wizard double"));
        SpellVfx.Impact(transform.position+Vector3.up*.7f,definition.tint,1);
    }
    private void Update()
    {
        if (!isServer || spent || definition == null) return;
        if (!NetworkServer.spawned.TryGetValue(ownerId,out var owner)) { Finish(false);return; }
        var ownerHealth=owner.GetComponentInChildren<Health>();
        if(ownerHealth==null||ownerHealth.IsDead||NetworkTime.time>=expiresAt){Finish(false);return;}
        if(definition.kind==TacticalKind.IceMirror)
        {
            var entity=ownerHealth.transform;
            transform.SetPositionAndRotation(entity.position+entity.forward*1.5f,entity.rotation);
        }
        if(definition.kind==TacticalKind.SnowDecoy) MoveDecoy();
        if(NetworkTime.time<nextTick)return;
        nextTick=NetworkTime.time+.1;
        if(definition.kind==TacticalKind.FireSeal && Armed)
        {
            foreach(var target in Health.ServerInstances)
                if(EnemyInRange(target,1.25f)) { Detonate(); break; }
        }
        else if(definition.kind==TacticalKind.GravityWell)
        {
            foreach(var target in Health.ServerInstances)
            {
                if(!EnemyInRange(target,definition.radius))continue;
                Vector3 delta=Vector3.ProjectOnPlane(transform.position-target.transform.position,Vector3.up);
                if(delta.magnitude>.45f)target.GetComponent<RelativeMovement>()?.ServerAddExternalForce(delta.normalized*2.5f);
            }
        }
    }
    private bool EnemyInRange(Health health,float radius)
    {
        if(health==null||health.IsDead||health.netId==ownerId)return false;
        Vector3 delta=health.transform.position-transform.position;
        if(Mathf.Abs(delta.y)>3 || Vector3.ProjectOnPlane(delta,Vector3.up).sqrMagnitude>radius*radius)return false;
        Vector3 from=transform.position+Vector3.up*.5f;
        if(Physics.Linecast(from,health.transform.position+Vector3.up*.4f,out var hit,~(1<<2),QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<Health>()==health;
        return true;
    }
    private void MoveDecoy()
    {
        Vector3 step=transform.forward*(5*Time.deltaTime);
        foreach(var hit in Physics.CapsuleCastAll(transform.position+Vector3.up*.45f,transform.position+Vector3.up*1.8f,.35f,transform.forward,step.magnitude+.05f,~(1<<2),QueryTriggerInteraction.Ignore))
            if(!hit.collider.transform.IsChildOf(transform)&&hit.collider.GetComponentInParent<Health>()?.netId!=ownerId&&hit.normal.y<.6f)return;
        Vector3 next=transform.position+step;
        if(Physics.Raycast(next+Vector3.up*.5f,Vector3.down,out var floor,1,~(1<<2),QueryTriggerInteraction.Ignore)&&floor.normal.y>.6f)
            transform.position=new Vector3(next.x,floor.point.y+.05f,next.z);
    }
    [Server] private void Detonate()
    {
        if(spent)return;
        foreach(var target in Health.ServerInstances)
            if(EnemyInRange(target,definition.radius))target.TakeSpellDamage(definition.damage,ownerId);
        Finish(true);
    }
    [Server] public void ProjectileHit(uint attacker)
    {
        if(spent||definition.kind!=TacticalKind.SnowDecoy||attacker==ownerId)return;
        Health closest=null;float distance=float.MaxValue;
        foreach(var health in Health.ServerInstances)
        {
            if(!EnemyInRange(health,3))continue;
            float d=(health.transform.position-transform.position).sqrMagnitude;
            if(d<distance){closest=health;distance=d;}
        }
        closest?.GetComponent<RelativeMovement>()?.ApplySlow(.5f,2);
        Finish(true);
    }
    [Server] private void Finish(bool burst)
    {
        if(spent)return;spent=true;
        if(definition.kind==TacticalKind.SnowDecoy)
        {
            if(NetworkClient.active && NetworkServer.active)CollapseDecoy();
            RpcCollapseDecoy();
        }
        else if(burst)RpcShatter(transform.position+Vector3.up*.4f);
        NetworkServer.Destroy(gameObject);
    }
    [ClientRpc] private void RpcShatter(Vector3 position)=>SpellVfx.Impact(position,definition.tint,1.4f);

    // Called by all three projectile implementations before applying damage.
    public static bool TryReflect(Collider hit,Transform projectile,uint incomingOwner,out uint reflectedOwner)
    {
        reflectedOwner=incomingOwner;
        var mirror=hit.GetComponentInParent<TacticalEffect>();
        if(mirror==null||!mirror.isServer||!mirror.CanBeHit(incomingOwner)||mirror.definition.kind!=TacticalKind.IceMirror)return false;
        var body=projectile.GetComponent<Rigidbody>();if(body==null)return false;
        Vector3 direction=-body.linearVelocity.normalized;
        if(NetworkServer.spawned.TryGetValue(incomingOwner,out var original))
        {
            var health=original.GetComponentInChildren<Health>();
            if(health!=null)direction=(health.transform.position+Vector3.up*.5f-projectile.position).normalized;
        }
        if(direction.sqrMagnitude<.01f)direction=-projectile.forward;
        reflectedOwner=mirror.ownerId;
        body.linearVelocity=direction*Mathf.Max(1,body.linearVelocity.magnitude);
        projectile.rotation=Quaternion.LookRotation(direction);
        projectile.position+=direction*.15f;
        foreach(var health in Health.ServerInstances)
            foreach(var targetCollider in health.GetComponentsInChildren<Collider>())
                foreach(var sourceCollider in projectile.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(sourceCollider,targetCollider,health.netId==reflectedOwner);
        mirror.Finish(true);
        return true;
    }
}



