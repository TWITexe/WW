using Mirror;
using UnityEngine;

// обрабатывает серверное попадание огненного шара и отправляет клиентам визуальный эффект.
public class FireballProjectile : NetworkBehaviour
{
    [SerializeField] private int fireballDamage = 20;
    [SerializeField, Min(0.1f)] private float lifetime = 8f;
    private double expiresAt;
    private bool consumed;
    private Vector3 previous;
    [SyncVar] public uint ownerId;
    // задаём срок жизни и исходную точку для проверки всего пути снаряда.
    public override void OnStartServer() { expiresAt = NetworkTime.time + lifetime;previous=transform.position; }
    // отключаем физическое движение у удалённых копий, получающих положение с сервера.
    public override void OnStartClient()
    {
        if (!isServer) GetComponent<Rigidbody>().isKinematic = true;
    }
    // проверяем пройденный путь на сервере и удаляем снаряд по таймеру.
    private void Update()
    {
        if(!isServer||consumed)return;
        if (ProjectileContact.Sweep(transform, ownerId, previous, out var hit, out var point)) Impact(hit, point);
        previous=transform.position;
        if (!consumed&&NetworkTime.time >= expiresAt) NetworkServer.Destroy(gameObject);
    }
    // защищаемся от повторного попадания, проверяем зеркало и наносим урон допустимой цели.
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
        Health health = other.GetComponentInParent<Health>();
        if (health != null) health.TakeSpellDamage(fireballDamage, ownerId, SpellDamage.IsHeadshot(health, other.ClosestPoint(transform.position)));
        // хост воспроизводит эффект сразу: после уничтожения снаряда его rpc уже не найдёт объект.
        if (NetworkClient.active) ShowImpact(point);
        RpcImpact(point);
        NetworkServer.Destroy(gameObject);
    }
    // показываем клиентам огненную вспышку в точке столкновения.
    [ClientRpc] private void RpcImpact(Vector3 position)
    {
        if (!isServer) ShowImpact(position);
    }
    // отдельный локальный запуск используется хостом и удалёнными клиентами.
    private void ShowImpact(Vector3 position) => SpellVfx.Impact(position, new Color(1, .3f, .04f), .7f, SpellHitKind.Fire);
}

