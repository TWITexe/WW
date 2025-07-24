using UnityEngine;

public class OrbitCamera : MonoBehaviour
{


    [SerializeField] Transform target;
    [SerializeField] float rotSpeed = 1.5f;
    [SerializeField] float minVerticalAngle = -30f;
    [SerializeField] float maxVerticalAngle = 60f;

    // зум камеры
    [SerializeField] float zoomSpeed = 2f;
    [SerializeField] float minZoom = 2f;
    [SerializeField] float maxZoom = 15f;
    private float currentZoom;

    private float rotY;
    private float rotX;
    private Vector3 offset;
    private Vector3 offsetDir; // направление от цели до камеры
    // для тряски
    private Vector3 shakeOffset = Vector3.zero;

    void Start()
    {

        Cursor.lockState = CursorLockMode.Locked;   // зафиксировать курсор в центре экрана
        Cursor.visible = false;

        rotY = transform.eulerAngles.y;
        rotX = transform.eulerAngles.x;
        currentZoom = offset.magnitude;
        offset = target.position - transform.position;
        offsetDir = offset.normalized;
    }

    void LateUpdate()
    {
        float horInput = Input.GetAxis("Horizontal");
        float mouseX = Input.GetAxis("Mouse X") * rotSpeed * 3;
        float mouseY = Input.GetAxis("Mouse Y") * rotSpeed * 3;



        if (Mathf.Approximately(horInput, 0))
        {
            rotY += mouseX;
            rotX -= mouseY;
        }
        else
        {
            rotY += horInput * rotSpeed;
        }

        rotX = Mathf.Clamp(rotX, minVerticalAngle, maxVerticalAngle);

        CameraZoom();

        Quaternion rotation = Quaternion.Euler(rotX, rotY, 0);
        Vector3 offset = offsetDir * currentZoom;

        transform.position = target.position - (rotation * offset) + shakeOffset;

        transform.LookAt(target);
    }

    private void CameraZoom()
    {
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        currentZoom -= scrollInput * zoomSpeed;
        currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
    }
    public void SetShakeOffset(Vector3 offset)
    {
        shakeOffset = offset;
    }
}
