using Mirror;
using UnityEngine;

public class PlayerNetworkCaster : NetworkBehaviour
{
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject fireballPrefab;

    [Command]
    public void CmdCastFireball(float speed)
    {
        if (fireballPrefab == null) return;
        // создаём снаряд на сервере
        GameObject fireball = Instantiate(fireballPrefab, firePoint.position + transform.forward, Quaternion.identity);

        // двигаем в направлении игрока
        Rigidbody rb = fireball.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = transform.forward * speed;

        // спавним для всех
        NetworkServer.Spawn(fireball);
    }
}
