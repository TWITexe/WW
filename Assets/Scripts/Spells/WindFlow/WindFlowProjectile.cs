using Mirror;
using UnityEngine;

// отталкивает поражённого персонажа и сохраняет авторство воздействия для возможного зачёта убийства.
public class WindFlowProjectile : NetworkBehaviour
{
    [SerializeField] private int windFlowForce = 2;
    [SerializeField, Min(0.1f)] private float lifetime = 8f;
    private double expiresAt;
    private bool consumed;
    private Vector3 previous;
    [SyncVar] public uint ownerId;
    // задаём срок жизни и точку начала непрерывной проверки столкновений.
    public override void OnStartServer() { expiresAt = NetworkTime.time + lifetime;previous=transform.position; }
    // удалённые копии следуют серверу без собственной симуляции движения Rigidbody.
    public override void OnStartClient()
    {
        if (!isServer) GetComponent<Rigidbody>().isKinematic = true;
    }
    // на сервере проверяем пройденный путь и истечение времени жизни.
    private void Update()
    {
        if(!isServer||consumed)return;
        if (ProjectileContact.Sweep(transform, ownerId, previous, out var hit, out var point)) Impact(hit, point);
        previous=transform.position;
        if (!consumed&&NetworkTime.time >= expiresAt) NetworkServer.Destroy(gameObject);
    }
    // после проверки отражения запоминаем атакующего и передаём живой цели импульс без прямого урона.
    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || consumed || !ProjectileContact.CanHit(other, transform, ownerId)) return;
        Impact(other, ProjectileContact.ImpactPosition(other, transform, previous));
    }

    // графика создаётся при любом допустимом столкновении, даже если у объекта нет здоровья.
    private void Impact(Collider other, Vector3 point)
    {
        if (!isServer || consumed || !ProjectileContact.CanHit(other,transform,ownerId)) return;
        if (TacticalEffect.TryReflect(other,transform,ownerId,out uint reflected)) { ownerId=reflected;previous=transform.position;return; }
        other.GetComponentInParent<TacticalEffect>()?.ProjectileHit(ownerId);
        consumed = true;
        RelativeMovement target = other.GetComponentInParent<RelativeMovement>();
        Health health = other.GetComponentInParent<Health>();
        if (health != null) health.RecordAttacker(ownerId);
        if (target != null && (health == null || !health.IsDead))
            target.ServerAddExternalForce(transform.forward * windFlowForce);
        // хост воспроизводит эффект сразу: после уничтожения снаряда его rpc уже не найдёт объект.
        if (NetworkClient.active) ShowImpact(point);
        RpcImpact(point);
        NetworkServer.Destroy(gameObject);
    }
    // показываем клиентам воздушную вспышку в точке столкновения.
    [ClientRpc] private void RpcImpact(Vector3 position)
    {
        if (!isServer) ShowImpact(position);
    }
    // отдельный локальный запуск используется хостом и удалёнными клиентами.
    private void ShowImpact(Vector3 position) => SpellVfx.Impact(position, new Color(.65f, 1, .9f), .8f);
}
