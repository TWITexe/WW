using Mirror;
using UnityEngine;

public class FireballProjectile : NetworkBehaviour
{
    [SerializeField] private int fireballDamage = 20;

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return; // только сервер обрабатывает урон

        Health health = other.GetComponent<Health>();
        if (health != null)
        {
            health.TakeDamage(fireballDamage);
            NetworkServer.Destroy(gameObject);
        }
    }
}
