using UnityEngine;

public class BillboardToCamera : MonoBehaviour
{
    private Camera targetCamera;

    private void Start()
    {
        targetCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            return;
        }

        // Поворачиваем Canvas лицом к камере
        transform.LookAt(transform.position + targetCamera.transform.forward);
    }
}