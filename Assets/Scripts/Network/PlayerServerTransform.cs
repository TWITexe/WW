using Mirror;
using UnityEngine;

// наблюдатели используют интерполяцию Mirror, а владелец — коррекции из RelativeMovement.
public class PlayerServerTransform : NetworkTransformReliable
{
    protected override void Configure()
    {
        base.Configure();
        syncDirection = SyncDirection.ServerToClient;
        useFixedUpdate = false;
    }

    protected override void UpdateClient()
    {
        if (!isOwned) base.UpdateClient();
    }

    protected override void OnServerToClientSync(Vector3? position, Quaternion? rotation, Vector3? scale)
    {
        if (!isOwned) base.OnServerToClientSync(position, rotation, scale);
    }

    protected override void OnTeleport(Vector3 destination, Quaternion rotation)
    {
        // состояния владельца содержат номер жизни, поэтому старый пакет не отменит респавн.
        if (isClient && isOwned && !isServer) return;
        var controller = GetComponent<CharacterController>();
        bool enabledBefore = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        base.OnTeleport(destination, rotation);
        if (controller != null) controller.enabled = enabledBefore;
    }
}
