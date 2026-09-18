using UnityEngine;
using UnityEngine.UI;

// передаёт выбранный в меню цвет в локальные настройки игрока.
public class ColorButton : MonoBehaviour
{
    [SerializeField] private PlayerColorId colorId;
    [SerializeField] private GameObject selectedMark;
    private LocalPlayerSettings settings;

    public PlayerColorId ColorId => colorId;

    // подключаем выбор цвета к нажатию кнопки.
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(SelectColor);
    }

    // при повторном включении окна восстанавливаем отметку текущего цвета.
    private void OnEnable() => BindSettings();

    // к этому моменту настройки уже прошли awake независимо от порядка объектов сцены.
    private void Start() => BindSettings();

    // подписываемся один раз: обновление галочки не требует проверки настроек каждый кадр.
    private void BindSettings()
    {
        if (settings != null || LocalPlayerSettings.Instance == null) return;
        settings = LocalPlayerSettings.Instance;
        settings.PreferredColorChanged += RefreshSelection;
        RefreshSelection(settings.CosmeticSettings.preferredColor);
    }

    // снимаем подписку при закрытии или уничтожении окна, чтобы настройки не удерживали старые кнопки.
    private void OnDisable()
    {
        if (settings == null) return;
        settings.PreferredColorChanged -= RefreshSelection;
        settings = null;
    }

    // у всех остальных цветов отметка выключается тем же событием выбора.
    public void RefreshSelection(PlayerColorId selectedColor)
    {
        if (selectedMark != null) selectedMark.SetActive(selectedColor == colorId);
    }

    // сохраняем предпочтение; доступность цвета проверяется позже на сервере.
    private void SelectColor()
    {
        LocalPlayerSettings.Instance.SetPreferredColor(colorId);
    }

#if UNITY_EDITOR
    // связываем сохранённую галочку с кнопкой при установке оформления в редакторе.
    public void ConfigureSelectionMark(GameObject mark) => selectedMark = mark;
#endif
}
