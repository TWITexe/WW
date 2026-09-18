using UnityEngine;

// хранит выбранные стихии и внешний вид локального игрока при переходах между сценами.
public class LocalPlayerSettings : MonoBehaviour
{
    public static LocalPlayerSettings Instance { get; private set; }

    public PlayerCosmeticSettings CosmeticSettings { get; private set; }
    public ElementLoadout Loadout { get; private set; }
    public event System.Action<PlayerColorId> PreferredColorChanged;

    // оставляем один экземпляр настроек и загружаем сохранённый набор стихий с проверкой корректности.
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Loadout = new ElementLoadout
        {
            q = (MagicElement)PlayerPrefs.GetInt("Elements.Q", 0),
            e = (MagicElement)PlayerPrefs.GetInt("Elements.E", 1),
            r = (MagicElement)PlayerPrefs.GetInt("Elements.R", 2)
        };
        if (!Loadout.IsValid) Loadout = ElementLoadout.Default;

        CosmeticSettings = new PlayerCosmeticSettings
        {
            preferredColor = PlayerColorId.Blue,
            nickname = "Player"
        };

        DontDestroyOnLoad(gameObject);
    }

    // запоминаем желаемый цвет; свободный цвет в матче окончательно назначит сервер.
    public void SetPreferredColor(PlayerColorId colorId)
    {
        if (CosmeticSettings.preferredColor == colorId) return;
        CosmeticSettings.preferredColor = colorId;
        PreferredColorChanged?.Invoke(colorId);
    }

    // сохраняем введённое имя в настройках текущего сеанса.
    public void SetNickname(string nickname)
    {
        CosmeticSettings.nickname = nickname;
    }

    // вне матча меняем привязку стихии и сохраняем все три слота в PlayerPrefs.
    public void SetElement(int slot, MagicElement element)
    {
        if (Mirror.NetworkClient.active || Mirror.NetworkServer.active) return;
        ElementLoadout updated = Loadout;
        updated.Assign(slot, element);
        if (!updated.IsValid) return;
        Loadout = updated;
        PlayerPrefs.SetInt("Elements.Q", (int)updated.q);
        PlayerPrefs.SetInt("Elements.E", (int)updated.e);
        PlayerPrefs.SetInt("Elements.R", (int)updated.r);
        PlayerPrefs.Save();
    }

    // очищаем общую ссылку только при уничтожении основного экземпляра настроек.
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
