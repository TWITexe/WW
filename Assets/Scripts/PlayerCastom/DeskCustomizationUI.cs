using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// оставляет интерфейс на планшете видимым, но разрешает ввод только в ракурсе настройки персонажа.
public class DeskCustomizationUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup inputGroup;
    [SerializeField] private GraphicRaycaster raycaster;
    [SerializeField] private BoxCollider hatHitArea;

    public BoxCollider HatHitArea => hatHitArea;
    public bool IsInteractive => inputGroup.interactable && raycaster.enabled;

    // блокируем ввод до того, как контроллер камеры завершит подлёт.
    private void Awake() => SetInteraction(false);

    // меняем доступность ввода без выключения холста и без изменения его прозрачности.
    public void SetInteraction(bool enabled)
    {
        inputGroup.interactable = enabled;
        inputGroup.blocksRaycasts = enabled;
        raycaster.enabled = enabled;
        if (!enabled && EventSystem.current != null)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

#if UNITY_EDITOR
    // сохраняем ссылки на холст и шляпу, чтобы в игре не искать объекты сцены.
    public void Configure(CanvasGroup group, GraphicRaycaster inputRaycaster, BoxCollider hat)
    {
        inputGroup = group;
        raycaster = inputRaycaster;
        hatHitArea = hat;
        SetInteraction(false);
    }
#endif
}
