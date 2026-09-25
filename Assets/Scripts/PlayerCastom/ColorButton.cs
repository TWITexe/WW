using UnityEngine;
using UnityEngine.UI;
using TMPro;

// передаёт выбранный в меню цвет в локальные настройки игрока.
public class ColorButton : MonoBehaviour
{
    [SerializeField] private PlayerColorId colorId;
    [SerializeField] private GameObject selectedMark;
    private LocalPlayerSettings settings;
    private EconomyClient economy;
    [SerializeField] private GameObject purchaseLock, priceCoin;
    [SerializeField] private TMP_Text priceLabel;

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
        if (settings == null && LocalPlayerSettings.Instance != null)
        {
            settings = LocalPlayerSettings.Instance;
            settings.PreferredColorChanged += RefreshSelection;
        }
        if (economy == null && EconomyClient.Instance != null)
        {
            economy = EconomyClient.Instance;
            economy.Changed += Refresh;
        }
        Refresh();
    }

    // снимаем подписку при закрытии или уничтожении окна, чтобы настройки не удерживали старые кнопки.
    private void OnDisable()
    {
        if (settings != null) settings.PreferredColorChanged -= RefreshSelection;
        if (economy != null) economy.Changed -= Refresh;
        settings = null;
        economy = null;
    }

    // у всех остальных цветов отметка выключается тем же событием выбора.
    public void RefreshSelection(PlayerColorId selectedColor)
    {
        Refresh();
    }

    private void Refresh()
    {
        bool owned = ShopCatalog.Allows(economy?.Profile, colorId);
        bool selected = owned && settings != null && settings.SelectedColor == colorId;
        var item = ShopCatalog.Find(ShopCatalog.ColorId(colorId));
        if (selectedMark != null) selectedMark.SetActive(selected);
        if (purchaseLock != null) purchaseLock.SetActive(!owned);
        if (priceCoin != null) priceCoin.SetActive(!owned);
        if (priceLabel != null) priceLabel.text = selected ? "Выбран" : owned ? (item?.price == 0 ? "" : "Выбрать") : $"{item?.price ?? 0} W";
        GetComponent<Button>().interactable = !Mirror.NetworkClient.active && !Mirror.NetworkServer.active &&
            (owned || (economy != null && economy.Connected && !economy.Busy));
    }

    // сохраняем предпочтение; доступность цвета проверяется позже на сервере.
    private void SelectColor()
    {
        if (Mirror.NetworkClient.active || Mirror.NetworkServer.active) return;
        if (ShopCatalog.Allows(economy?.Profile, colorId)) LocalPlayerSettings.Instance?.SetPreferredColor(colorId);
        else if (economy != null)
        {
            var purchasedColor = colorId;
            economy.Purchase(ShopCatalog.ColorId(colorId), success =>
            {
                if (success) LocalPlayerSettings.Instance?.SetPreferredColor(purchasedColor);
            });
        }
    }

#if UNITY_EDITOR
    // связываем сохранённую галочку с кнопкой при установке оформления в редакторе.
    public void ConfigureSelectionMark(GameObject mark) => selectedMark = mark;
    public void ConfigureShop(GameObject mark, GameObject locked, TMP_Text price, GameObject coin)
    { selectedMark = mark; purchaseLock = locked; priceLabel = price; priceCoin = coin; }
#endif
}
