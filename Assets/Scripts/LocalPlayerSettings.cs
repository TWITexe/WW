using UnityEngine;

// хранит выбранные стихии и внешний вид локального игрока при переходах между сценами.
public class LocalPlayerSettings : MonoBehaviour
{
    public static LocalPlayerSettings Instance { get; private set; }

    public PlayerCosmeticSettings CosmeticSettings { get; private set; }
    public ElementLoadout Loadout { get; private set; }
    public SavedElementBuilds SavedBuilds { get; private set; }
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
        SavedBuilds = new SavedElementBuilds();

        CosmeticSettings = new PlayerCosmeticSettings
        {
            preferredColor = ReadSavedColor(),
            nickname = PlayerPrefs.GetString("Player.Nickname", "Player")
        };

        DontDestroyOnLoad(gameObject);
    }

    // запоминаем желаемый цвет; свободный цвет в матче окончательно назначит сервер.
    public void SetPreferredColor(PlayerColorId colorId)
    {
        if (colorId == PlayerColorId.None || !System.Enum.IsDefined(typeof(PlayerColorId), colorId)) return;
        if (CosmeticSettings.preferredColor == colorId) return;
        CosmeticSettings.preferredColor = colorId;
        PlayerPrefs.SetInt("Player.Color", (int)colorId);
        PlayerPrefs.Save();
        PreferredColorChanged?.Invoke(colorId);
    }

    // повреждённое или устаревшее значение заменяем стандартным цветом.
    public static PlayerColorId ReadSavedColor()
    {
        var color = (PlayerColorId)PlayerPrefs.GetInt("Player.Color", (int)PlayerColorId.Blue);
        return color != PlayerColorId.None && System.Enum.IsDefined(typeof(PlayerColorId), color) ? color : PlayerColorId.Blue;
    }

    // сохраняем введённое имя на этом компьютере между запусками игры.
    public void SetNickname(string nickname)
    {
        CosmeticSettings.nickname = nickname ?? "";
        PlayerPrefs.SetString("Player.Nickname", CosmeticSettings.nickname);
    }

    // записываем настройки на диск при завершении редактирования или выходе из приложения.
    public void SaveNickname() => PlayerPrefs.Save();
    private void OnApplicationPause(bool paused) { if (paused) SaveNickname(); }
    private void OnApplicationQuit() => SaveNickname();

    // вне матча меняем привязку стихии и сохраняем все три слота в PlayerPrefs.
    public void SetElement(int slot, MagicElement element)
    {
        if (Mirror.NetworkClient.active || Mirror.NetworkServer.active) return;
        ElementLoadout updated = Loadout;
        updated.Assign(slot, element);
        ApplyLoadout(updated);
    }

    // Применяем билд целиком: последовательные Assign могли бы переставить уже выбранные слоты.
    public bool ApplyLoadout(ElementLoadout updated)
    {
        if (Mirror.NetworkClient.active || Mirror.NetworkServer.active || !updated.IsValid) return false;
        Loadout = updated;
        PlayerPrefs.SetInt("Elements.Q", (int)updated.q);
        PlayerPrefs.SetInt("Elements.E", (int)updated.e);
        PlayerPrefs.SetInt("Elements.R", (int)updated.r);
        PlayerPrefs.Save();
        return true;
    }

    // очищаем общую ссылку только при уничтожении основного экземпляра настроек.
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
