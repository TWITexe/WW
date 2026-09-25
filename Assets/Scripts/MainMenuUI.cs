using Mirror;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// обрабатывает создание комнаты, запуск сетевых режимов и открытие окон меню.
public partial class MainMenuUI : MonoBehaviour
{
    [SerializeField] private RoomNetworkDiscovery discovery;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Button confirmRoomButton;

    // подключаем проверку имени комнаты и подтверждение через поле ввода.
    private void Start()
    {
        BindRoomOptions();
        if (roomNameInput != null)
        {
            roomNameInput.characterLimit = 32;
            roomNameInput.onValueChanged.AddListener(UpdateRoomButton);
            roomNameInput.onSubmit.AddListener(_ => ConfirmCreateRoom());
        }
    }

    // окно создания комнаты находится отдельно от connectui, поэтому закрываем его по esc самостоятельно.
    private void Update()
    {
        UpdateConnectionStatus();
        if (createRoomPanel != null && createRoomPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            CancelCreateRoom();
    }

    // разрешаем создание комнаты только при непустом имени.
    private void UpdateRoomButton(string value)
    {
        if (confirmRoomButton != null) confirmRoomButton.interactable = !string.IsNullOrWhiteSpace(value) && ValidRoomOptions();
    }

    // открываем диалог с последним именем комнаты; хост пока не запускаем.
    public void CreateRoom()
    {
        if (NetworkClient.active || NetworkServer.active || RoomMigration.Active) return;
        if (createRoomPanel == null || roomNameInput == null)
        {
            Debug.LogError("Assign the saved Create Room dialog in MainMenuUI.");
            return;
        }
        createRoomPanel.SetActive(true);
        ResetRoomOptions();
        string nickname = LocalPlayerSettings.Instance != null ? LocalPlayerSettings.Instance.CosmeticSettings.nickname : "Player";
        roomNameInput.text = PlayerPrefs.GetString("LastRoomName", "Комната " + nickname);
        UpdateRoomButton(roomNameInput.text);
        roomNameInput.Select();
        roomNameInput.ActivateInputField();
    }

    // закрываем диалог без создания сетевой сессии.
    public void CancelCreateRoom()
    {
        if (roomPasswordInput != null) roomPasswordInput.text = "";
        if (createRoomPanel != null) createRoomPanel.SetActive(false);
    }

    // проверяем имя, сохраняем его, запускаем хост и объявляем комнату в локальной сети.
    public void ConfirmCreateRoom()
    {
        if (createRoomPanel == null || !createRoomPanel.activeInHierarchy || roomNameInput == null || NetworkClient.active || NetworkServer.active) return;
        string name = roomNameInput.text.Trim();
        if (string.IsNullOrWhiteSpace(name) || !ValidRoomOptions()) return;
        if (name.Length > 32) name = name.Substring(0, 32);
        if (discovery == null || NetworkManager.singleton == null)
        {
            Debug.LogError("Room creation requires NetworkManager and RoomNetworkDiscovery.");
            return;
        }
        discovery.SetRoomName(name);
        var rules = new MatchRules { minutes = int.Parse(matchMinutesInput.text), killGoal = int.Parse(killGoalInput.text) };
        NetManager.Room.ConfigureRoom(name, rules, privateRoomToggle.isOn ? roomPasswordInput.text : "");
        PlayerPrefs.SetInt("Room.Minutes", rules.minutes);
        PlayerPrefs.SetInt("Room.KillGoal", rules.killGoal);
        roomPasswordInput.text = "";
        PlayerPrefs.SetString("LastRoomName", name);
        PlayerPrefs.Save();
        createRoomPanel.SetActive(false);
        NetworkManager.singleton.StartHost();

        if (discovery != null)
            discovery.AdvertiseServer();
    }

    // запускаем клиент с адресом, уже заданным в NetworkManager.
    public void ConnectToServer()
    {
        NetworkManager.singleton.StartClient();
    }

    // запускаем выделенный сервер и объявляем его через поиск комнат.
    public void CreateServer()
    {
        NetworkManager.singleton.StartServer();

        if (discovery != null)
            discovery.AdvertiseServer();
    }

    // скрываем окно, переданное обработчиком кнопки.
    public void CloseWindow(GameObject window)
    {
        window.SetActive(false);
    }
    // показываем окно, переданное обработчиком кнопки.
    public void OpenWindow(GameObject window)
    {
        window.SetActive(true);
    }
}
