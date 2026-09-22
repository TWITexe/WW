using Mirror;
using UnityEngine;

// дымовой заряд летит по дуге; полноценная завеса раскрывается только после первого столкновения.
public class ArcSmokeProjectile : NetworkBehaviour
{
    [SyncVar] private bool flying;
    [SyncVar] private Vector3 velocity;
    public GameObject projectileVisual;
    private ElementalEffect effect;
    private double launchedAt;
    public bool Flying => flying;
    private void Awake() => effect = GetComponent<ElementalEffect>();
    public void Launch(Vector3 initialVelocity) { flying = true; velocity = initialVelocity; }
    public override void OnStartServer() => launchedAt = NetworkTime.time;
    private void FixedUpdate()
    {
        if (!isServer || !flying) return;
        Vector3 previous=transform.position;
        velocity += Vector3.down * (9.81f*Time.fixedDeltaTime);
        transform.position += velocity*Time.fixedDeltaTime;
        if (ProjectileContact.Sweep(transform,effect.ownerId,previous,out _,out var point,.25f))
        {
            transform.position=point;
            flying=false;
            velocity=Vector3.zero;
            effect.BeginAreaLifetime();
        }
        else if (NetworkTime.time-launchedAt > 6) NetworkServer.Destroy(gameObject);
    }
    private void LateUpdate()
    {
        if (projectileVisual != null) projectileVisual.SetActive(flying);
    }
}
