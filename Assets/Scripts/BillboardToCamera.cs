using Mirror;
using UnityEngine;

// поворачивает надпись над чужим игроком к камере и скрывает её у своего персонажа.
public class BillboardToCamera : MonoBehaviour
{
    private Camera targetCamera;
    private NetworkIdentity owner;
    private Canvas overheadCanvas;
    private bool canvasVisibleByDefault;

    // запоминаем владельца надписи и исходную видимость её холста.
    private void Awake()
    {
        owner = GetComponentInParent<NetworkIdentity>();
        overheadCanvas = GetComponent<Canvas>();
        canvasVisibleByDefault = overheadCanvas != null && overheadCanvas.enabled;
    }

    // после движения камеры обновляем видимость и разворачиваем надпись к зрителю.
    private void LateUpdate()
    {
        // скрываем только холст над головой, сохраняя данные для таблицы игроков.
        if (overheadCanvas != null)
            overheadCanvas.enabled = canvasVisibleByDefault && (owner == null || !owner.isLocalPlayer);
        if (owner != null && owner.isLocalPlayer) return;
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            targetCamera = Camera.main;
        if (targetCamera != null)
            transform.LookAt(transform.position + targetCamera.transform.forward);
    }
}
