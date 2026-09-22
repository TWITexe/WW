using Mirror;
using UnityEngine;

// графика следует за отображаемым магом, а серверный коллайдер сохраняет самостоятельную сетевую позицию.
[DefaultExecutionOrder(250)]
public class IceMirrorVisual : MonoBehaviour
{
    public Transform graphics;
    private TacticalEffect effect;
    private WizardAppearance ownerAppearance;
    private Quaternion displayedRotation;
    private Vector3 lastOwnerPosition;
    private bool poseReady;

    private void Awake() => effect = GetComponent<TacticalEffect>();

    private void LateUpdate()
    {
        if (graphics == null || effect == null || !effect.isClient) return;
        if (ownerAppearance == null && NetworkClient.spawned.TryGetValue(effect.ownerId, out var owner))
            ownerAppearance = owner.GetComponentInChildren<WizardAppearance>();
        if (ownerAppearance != null) FollowOwner();
    }

    // смещение содержит то же сглаживание и парение, что у тела; посох и шляпа на него не влияют.
    private void FollowOwner()
    {
        Transform body = ownerAppearance.transform;
        bool reset = !poseReady || (body.position - lastOwnerPosition).sqrMagnitude > 9;
        // сглаживаем и поворот: изменение физического yaw по тикам не должно качать рамку перед камерой.
        displayedRotation = reset ? body.rotation : Quaternion.Slerp(displayedRotation, body.rotation,
            1 - Mathf.Exp(-30 * Time.deltaTime));
        Vector3 position = body.position + displayedRotation * Vector3.forward * 1.5f + ownerAppearance.VisualDisplacement;
        graphics.SetPositionAndRotation(position, displayedRotation);
        lastOwnerPosition = body.position;
        poseReady = true;
    }
}
