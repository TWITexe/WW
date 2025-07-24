using Mirror;
using UnityEngine;

public class PlayerNetworkCaster : NetworkBehaviour
{
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject fireballPrefab;
    [SerializeField] private GameObject windFlowPrefab;

    [Command]
    public void CmdCastFireball(float speed)
    {
        if (fireballPrefab == null) return;
        // ������ ������ �� �������
        GameObject fireball = Instantiate(fireballPrefab, firePoint.position + transform.forward, Quaternion.identity);

        // ������� � ����������� ������
        Rigidbody rb = fireball.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = transform.forward * speed;

        // ������� ��� ����
        NetworkServer.Spawn(fireball);
    }

    [Command]
    public void CmdCastWindFlow(float speed)
    {
        if (windFlowPrefab == null) return;
        
        GameObject windFlow = Instantiate(windFlowPrefab, firePoint.position + transform.forward, Quaternion.identity);

        // ������� � ����������� ������
        Rigidbody rb = windFlow.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = transform.forward * speed;

        // ������� ��� ����
        NetworkServer.Spawn(windFlow);
    }
}
