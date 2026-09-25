using TMPro;
using UnityEngine;

public partial class PlayerGameUI
{
    [Header("Character vitals")]
    [SerializeField] private RectTransform vitalsRoot;
    [SerializeField] private CanvasGroup vitalsGroup;
    [SerializeField] private UnityEngine.UI.Image shieldFill;
    [SerializeField] private TMP_Text vitalsHealthText, vitalsMaxHealthText, vitalsShieldText, vitalsStaminaText;
    [SerializeField] private Vector2 vitalsScreenOffset = new Vector2(-150, -215);
    [SerializeField] private float vitalsAnchorHeight = .65f;
    private int shieldCapacity;

    private void OnEnable() => Canvas.willRenderCanvases += PositionVitals;

    private void ResetVitals()
    {
        displayedHealth = displayedMaxHealth = displayedShield = -1;
        shieldCapacity = 0;
    }

    private void UpdateShieldBar()
    {
        if (shieldFill == null) return;
        int value = health.IsDead ? 0 : Mathf.Max(0, health.Shield);
        // Keep the scale while damage consumes this shield; reset it when the shield expires.
        shieldCapacity = value == 0 ? 0 : Mathf.Max(shieldCapacity, value);
        shieldFill.fillAmount = shieldCapacity > 0 ? (float)value / shieldCapacity : 0;
        if (vitalsShieldText != null) vitalsShieldText.gameObject.SetActive(value > 0);
    }

    // Canvas callbacks run after the shoulder camera's LateUpdate, avoiding a one-frame lag.
    private void PositionVitals()
    {
        if (vitalsRoot == null || vitalsGroup == null || canvas == null) return;
        var view = caster != null ? caster.ViewCamera : null;
        bool visible = canvas.enabled && view != null && view.isActiveAndEnabled;
        if (!visible) { vitalsGroup.alpha = 0; return; }
        Vector3 anchor = caster.transform.position + Vector3.up * vitalsAnchorHeight;
        if (movement != null) anchor += movement.PresentationOffset;
        Vector3 screen = view.WorldToScreenPoint(anchor);
        if (screen.z <= view.nearClipPlane) { vitalsGroup.alpha = 0; return; }

        var parent = vitalsRoot.parent as RectTransform;
        if (parent == null) return;
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out var point)) return;
        point += vitalsScreenOffset;

        // Keep the complete block inside the safe area even when the camera moves into a wall.
        Rect safe = Screen.safeArea;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.min, uiCamera, out var min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.max, uiCamera, out var max);
        Vector2 size = vitalsRoot.rect.size;
        const float margin = 16;
        point.x = Mathf.Clamp(point.x, min.x + margin, Mathf.Max(min.x + margin, max.x - size.x - margin));
        point.y = Mathf.Clamp(point.y, min.y + margin, Mathf.Max(min.y + margin, max.y - size.y - margin));
        vitalsRoot.anchoredPosition = point;
        vitalsGroup.alpha = 1;
    }
}
