using Mirror;
using UnityEngine;

public class PlayerNetworkCaster : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject fireballPrefab;
    [SerializeField] private GameObject windFlowPrefab;

    [Command]
    public void CmdCastFireball(float speed, Vector3 direction)
    {
        if (fireballPrefab == null) return;
        // создаём снаряд на сервере
        GameObject fireball = Instantiate(fireballPrefab, firePoint.position + transform.forward, Quaternion.identity);

        // двигаем в направлении игрока
        Rigidbody rb = fireball.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = direction.normalized * speed;

        // спавним для всех
        NetworkServer.Spawn(fireball);
    }
    public void CastFireball(float speed)
    {
        Vector3 direction = playerCamera != null ? playerCamera.transform.forward + new Vector3(0,0.35f,0) : transform.forward;
        CmdCastFireball(speed, direction);
    }

    [Command]
    public void CmdCastWindFlow(float speed, Vector3 direction)
    {
        if (windFlowPrefab == null) return;

        GameObject windFlow = Instantiate(
       windFlowPrefab,
       firePoint.position + direction.normalized,
       Quaternion.identity);

        // двигаем в направлении игрока
        Rigidbody rb = windFlow.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = direction.normalized * speed;

        // спавним для всех
        NetworkServer.Spawn(windFlow);
    }
    public void CastWindFlow(float speed)
    {
        Vector3 direction = playerCamera != null ? playerCamera.transform.forward + new Vector3(0, 0.35f, 0) : transform.forward;
        CmdCastWindFlow(speed, direction);
    }
}
