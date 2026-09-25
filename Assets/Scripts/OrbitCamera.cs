using UnityEngine;

// ведёт камеру за плечом игрока и приближает её при появлении препятствий.
public class OrbitCamera : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] float rotSpeed = 1.5f;
    [SerializeField] float minVerticalAngle = -35;
    [SerializeField] float maxVerticalAngle = 65;
    [SerializeField] float pivotHeight = 1.6f;
    [SerializeField] float shoulderOffset = 1.6f;
    [SerializeField] float defaultDistance = 6.5f;
    [SerializeField] float initialPitch = 3;
    [SerializeField] float fieldOfView = 70;
    private float yaw, pitch;
    private Vector3 shakeOffset;
    private RelativeMovement movement;
    private PlayerUltimate ultimate;
    // задаём начальные углы, поле зрения и ближнюю плоскость камеры.
    private void Start()
    {
        movement = target != null ? target.GetComponent<RelativeMovement>() : null;
        ultimate = target != null ? target.GetComponent<PlayerUltimate>() : null;
        pitch = Mathf.Clamp(initialPitch, minVerticalAngle, maxVerticalAngle);
        yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
        var camera = GetComponent<Camera>();
        camera.fieldOfView = fieldOfView; camera.nearClipPlane = .1f;
    }
    // вращаем камеру мышью и проверяем путь до неё сферой, исключая игроков и собственную модель.
    private void LateUpdate()
    {
        if (target == null) return;
        if (!PlayerGameUI.InputBlocked)
        {
            yaw += Input.GetAxis("Mouse X") * rotSpeed;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * rotSpeed, minVerticalAngle, maxVerticalAngle);
        }
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        var controlled = ultimate != null ? ultimate.ControlledOrb : null;
        Vector3 pivot = controlled != null ? controlled.PresentationPosition + Vector3.up * .45f :
            target.position + Vector3.up * pivotHeight + (movement != null ? movement.PresentationOffset : Vector3.zero);
        Vector3 offset = rotation * new Vector3(controlled != null ? 0 : shoulderOffset, 0, -defaultDistance);
        float distance = offset.magnitude;
        foreach (var hit in Physics.SphereCastAll(pivot, .18f, offset.normalized, distance, ~(1 << 2), QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(target.root) && hit.collider.GetComponentInParent<Health>() == null &&
                (controlled == null || !hit.collider.transform.IsChildOf(controlled.transform)))
                distance = Mathf.Min(distance, Mathf.Max(.25f, hit.distance - .05f));
        transform.SetPositionAndRotation(pivot + offset.normalized * distance + shakeOffset, rotation);
    }
    // принимаем дополнительное смещение покачивания без изменения точки слежения.
    public void SetShakeOffset(Vector3 offset) => shakeOffset = offset;
}
