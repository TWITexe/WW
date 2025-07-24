using Mirror;
using UnityEngine;

public class WindFlowProjectile : NetworkBehaviour
{
    [SerializeField] private int windFlowForce = 20;
    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return; // только сервер обрабатывает урон

        Rigidbody rbEnemy = other.GetComponent<Rigidbody>();
        if (rbEnemy != null)
        {
            rbEnemy.AddForce(Vector3.back * windFlowForce, ForceMode.Impulse);

        }
        NetworkServer.Destroy(gameObject);
    }
}
