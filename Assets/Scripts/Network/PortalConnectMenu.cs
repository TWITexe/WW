using UnityEngine;
using UnityEngine.EventSystems;

// плавно показывает существующее окно подключения после подлёта камеры к порталу.
public class PortalConnectMenu : MonoBehaviour
{
    [SerializeField] private BoxCollider portalHitArea;
    [SerializeField] private CanvasGroup panel;
    [SerializeField, Min(.01f)] private float fadeDuration = .4f;
    private float elapsed;
    public static event System.Action Opened;

    public BoxCollider HitArea => portalHitArea;
    public CanvasGroup Panel => panel;

    // при запуске окно не перекрывает сцену и не принимает ввод.
    private void Awake() => Hide();

    // включаем окно с нулевой прозрачностью только после завершения движения камеры.
    public void Show()
    {
        elapsed = 0;
        panel.alpha = 0;
        SetInteraction(false);
        panel.gameObject.SetActive(true);
        // сообщаем списку комнат об открытии без поиска объектов сцены.
        Opened?.Invoke();
    }

    // контроллер камеры вызывает обновление только в ракурсе портала; время не зависит от паузы игры.
    public void UpdateAppearance(float deltaTime)
    {
        if (!panel.gameObject.activeSelf) return;
        elapsed = Mathf.Min(elapsed + deltaTime, fadeDuration);
        float progress = elapsed / fadeDuration;
        panel.alpha = progress * progress * (3f - 2f * progress);
        SetInteraction(progress >= 1);
    }

    // отключаем ввод во время появления и сетевого подключения, не меняя обработчики кнопок.
    public void SetInteraction(bool enabled)
    {
        panel.interactable = enabled;
        panel.blocksRaycasts = enabled;
    }

    // при уходе снимаем выделение поля ввода и скрываем только окно подключения.
    public void Hide()
    {
        if (panel == null) return;
        if (EventSystem.current != null)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(panel.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
        SetInteraction(false);
        panel.alpha = 0;
        panel.gameObject.SetActive(false);
        elapsed = 0;
    }

#if UNITY_EDITOR
    // сохраняем ссылки в сцене вместо поиска портала и окна во время игры.
    public void Configure(BoxCollider hitArea, CanvasGroup window)
    {
        portalHitArea = hitArea;
        panel = window;
        Hide();
    }
#endif
}
