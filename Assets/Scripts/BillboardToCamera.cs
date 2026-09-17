using Mirror;
using UnityEngine;

public class BillboardToCamera : MonoBehaviour
{
    private Camera targetCamera;
    private NetworkIdentity owner;
    private Canvas overheadCanvas;
    private bool canvasVisibleByDefault;

    private void Awake()
    {
        owner = GetComponentInParent<NetworkIdentity>();
        overheadCanvas = GetComponent<Canvas>();
        canvasVisibleByDefault = overheadCanvas != null && overheadCanvas.enabled;
    }

    private void LateUpdate()
    {
        // Keep data updated for the scoreboard; hide only the overhead canvas.
        if (overheadCanvas != null)
            overheadCanvas.enabled = canvasVisibleByDefault && (owner == null || !owner.isLocalPlayer);
        if (owner != null && owner.isLocalPlayer) return;
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            targetCamera = Camera.main;
        if (targetCamera != null)
            transform.LookAt(transform.position + targetCamera.transform.forward);
    }
}
