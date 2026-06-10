using Mirror;
using UnityEngine;

public class WindFlowProjectile : NetworkBehaviour
{
    [SerializeField] private int windFlowForce = 2;

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return; // только сервер обрабатывает попадание

        RelativeMovement target = other.GetComponentInParent<RelativeMovement>();

        if (target != null)
        {
            // направление от снаряда к игроку
            Vector3 dir = (other.transform.position - transform.position).normalized;

            // сервер говорит владельцу игрока: "тебя надо оттолкнуть"
            target.ServerAddExternalForce(dir * windFlowForce);
        }

        NetworkServer.Destroy(gameObject);
    }
}