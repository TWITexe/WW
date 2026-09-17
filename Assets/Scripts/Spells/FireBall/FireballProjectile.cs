using Mirror;
using UnityEngine;

public class FireballProjectile : NetworkBehaviour
{
    [SerializeField] private int fireballDamage = 20;
    [SerializeField, Min(0.1f)] private float lifetime = 8f;
    private double expiresAt;
    private bool consumed;
    private Vector3 previous;
    [SyncVar] public uint ownerId;
    public override void OnStartServer() { expiresAt = NetworkTime.time + lifetime;previous=transform.position; }
    public override void OnStartClient()
    {
        if (!isServer) GetComponent<Rigidbody>().isKinematic = true;
    }
    private void Update()
    {
        if(!isServer||consumed)return;
        if(ProjectileContact.Sweep(transform,ownerId,previous,out var hit))OnTriggerEnter(hit);
        previous=transform.position;
        if (!consumed&&NetworkTime.time >= expiresAt) NetworkServer.Destroy(gameObject);
    }
    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || consumed || !ProjectileContact.CanHit(other,transform,ownerId)) return;
        if (TacticalEffect.TryReflect(other,transform,ownerId,out uint reflected)) { ownerId=reflected;previous=transform.position;return; }
        other.GetComponentInParent<TacticalEffect>()?.ProjectileHit(ownerId);
        consumed = true;
        Health health = other.GetComponentInParent<Health>();
        if (health != null) health.TakeSpellDamage(fireballDamage, ownerId, SpellDamage.IsHeadshot(health, other.ClosestPoint(transform.position)));
        RpcImpact(transform.position);
        NetworkServer.Destroy(gameObject);
    }
    [ClientRpc] private void RpcImpact(Vector3 position) => SpellVfx.Impact(position, new Color(1, .3f, .04f), .7f);
}

