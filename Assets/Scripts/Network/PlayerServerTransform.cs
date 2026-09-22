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

    protected override void OnTeleport(Vector3 destination) => OnTeleport(destination, target.rotation);

    protected override void OnTeleport(Vector3 destination, Quaternion rotation)
    {
        // состояния владельца содержат номер жизни, поэтому старый пакет не отменит респавн.
        if (isClient && isOwned && !isServer) return;
        var controller = GetComponent<CharacterController>();
        bool enabledBefore = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        // NetworkTransformBase.OnTeleport вызывает виртуальный ResetState, который
        // у Reliable обнуляет базы дельта-сжатия. Повторный RPC на хосте может
        // сбросить базу уже после отправки снимка: наблюдатели получат постоянное
        // смещение координат. При переносе очищаем только историю интерполяции.
        target.SetPositionAndRotation(destination, rotation);
        serverSnapshots.Clear();
        clientSnapshots.Clear();
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = enabledBefore;
    }
}
