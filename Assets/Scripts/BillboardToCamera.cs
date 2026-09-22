using Mirror;
using UnityEngine;

// поворачивает надпись над чужим игроком к камере и скрывает её у своего персонажа.
public class BillboardToCamera : MonoBehaviour
{
    private Camera targetCamera;
    private NetworkIdentity owner;
    private Canvas overheadCanvas;
    private bool canvasVisibleByDefault;
    private Health health;
    private readonly RaycastHit[] visibilityHits = new RaycastHit[32];

    // запоминаем владельца надписи и исходную видимость её холста.
    private void Awake()
    {
        owner = GetComponentInParent<NetworkIdentity>();
        health = GetComponentInParent<Health>();
        overheadCanvas = GetComponent<Canvas>();
        canvasVisibleByDefault = overheadCanvas != null && overheadCanvas.enabled;
    }

    // после движения камеры обновляем видимость и разворачиваем надпись к зрителю.
    private void LateUpdate()
    {
        // скрываем только холст над головой, сохраняя данные для таблицы игроков.
        if (owner != null && owner.isLocalPlayer) { if(overheadCanvas!=null)overheadCanvas.enabled=false; return; }
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            targetCamera = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponentInChildren<PlayerNetworkCaster>()?.ViewCamera : null;
        bool visible = targetCamera != null && (health == null || !health.IsDead) && CanSeePlayer();
        if (overheadCanvas != null) overheadCanvas.enabled = canvasVisibleByDefault && visible;
        if (targetCamera != null)
            transform.LookAt(transform.position + targetCamera.transform.forward);
    }

    // проверяем и тело, и верхнюю часть мага; низкое укрытие не прячет полностью видимую голову.
    private bool CanSeePlayer()
    {
        Vector3 center = health != null ? health.transform.position : transform.position;
        Vector3 screen = targetCamera.WorldToViewportPoint(center);
        if(screen.z <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1) return false;
        return ClearView(center) || ClearView(center + Vector3.up * .75f);
    }
    private bool ClearView(Vector3 point)
    {
        Vector3 origin=targetCamera.transform.position, delta=point-origin;
        int count=Physics.RaycastNonAlloc(origin,delta.normalized,visibilityHits,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==visibilityHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var hit=visibilityHits[i].collider;
            if(owner!=null && hit.transform.IsChildOf(owner.transform))continue;
            if(NetworkClient.localPlayer!=null && hit.transform.IsChildOf(NetworkClient.localPlayer.transform))continue;
            if(hit.GetComponentInParent<Health>()!=null)continue;
            return false;
        }
        return true;
    }
}
