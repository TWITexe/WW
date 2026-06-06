using Mirror;
using UnityEngine;

public class WindFlowProjectile : NetworkBehaviour
{
    [SerializeField] private int windFlowForce = 2;
    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return; // только сервер обрабатывает урон

        RelativeMovement target = other.GetComponent<RelativeMovement>();
        if (target != null)
        {
            Vector3 dir = (other.transform.position - transform.position).normalized;
            target.AddExternalForce(dir * windFlowForce);

        }
        NetworkServer.Destroy(gameObject);
    }
}
