using Mirror;
using TMPro;
using UnityEngine;

public partial class MainMenuUI
{
    [SerializeField] private UnityEngine.UI.Toggle privateRoomToggle;
    [SerializeField] private TMP_InputField roomPasswordInput, matchMinutesInput, killGoalInput;
    [SerializeField] private TMP_Dropdown gameModeDropdown;
    [SerializeField] private UnityEngine.UI.Slider matchMinutesSlider, killGoalSlider;
    [SerializeField] private GameObject joinRoomPanel;
    [SerializeField] private TMP_InputField joinPasswordInput;
    [SerializeField] private TMP_Text joinTitle, joinStatus;
    [SerializeField] private UnityEngine.UI.Button joinConfirmButton;
    RoomInfo selectedRoom;
    bool connecting;

    void BindRoomOptions()
    {
        if (matchMinutesSlider != null) matchMinutesSlider.onValueChanged.AddListener(value => matchMinutesInput.text = ((int)value).ToString());
        if (killGoalSlider != null) killGoalSlider.onValueChanged.AddListener(value => killGoalInput.text = ((int)value).ToString());
        if (privateRoomToggle != null) privateRoomToggle.onValueChanged.AddListener(value =>
        {
            roomPasswordInput.interactable = value;
            if (!value) roomPasswordInput.text = "";
            UpdateRoomButton(roomNameInput.text);
        });
        foreach (var field in new[] { roomPasswordInput, matchMinutesInput, killGoalInput })
            if (field != null) field.onValueChanged.AddListener(_ => UpdateRoomButton(roomNameInput.text));
        if (joinPasswordInput != null) joinPasswordInput.onSubmit.AddListener(_ => ConfirmJoinRoom());
    }
    bool ValidRoomOptions() => privateRoomToggle != null &&
        (!privateRoomToggle.isOn || !string.IsNullOrWhiteSpace(roomPasswordInput.text)) &&
        int.TryParse(matchMinutesInput.text, out int minutes) && minutes >= 1 && minutes <= 30 &&
        int.TryParse(killGoalInput.text, out int kills) && kills >= 1 && kills <= 33;
    void ResetRoomOptions()
    {
        if (privateRoomToggle == null) return;
        privateRoomToggle.isOn = false;
        roomPasswordInput.text = "";
        roomPasswordInput.interactable = false;
        matchMinutesInput.text = Mathf.Clamp(PlayerPrefs.GetInt("Room.Minutes", 10), 1, 30).ToString();
        killGoalInput.text = Mathf.Clamp(PlayerPrefs.GetInt("Room.KillGoal", 15), 1, 33).ToString();
        matchMinutesSlider.SetValueWithoutNotify(int.Parse(matchMinutesInput.text));
        killGoalSlider.SetValueWithoutNotify(int.Parse(killGoalInput.text));
        gameModeDropdown.value = 0;
    }
    public void JoinRoom(RoomInfo room)
    {
        if (NetworkClient.active || NetworkServer.active || RoomMigration.Active) return;
        selectedRoom = room;
        RoomAuthenticator.LastError = null;
        joinPasswordInput.text = "";
        joinPasswordInput.gameObject.SetActive(room.isPrivate);
        joinTitle.text = room.roomName;
        joinTitle.richText = false;
        joinStatus.text = room.isPrivate ? "Введите пароль комнаты" : "Подключение…";
        joinConfirmButton.interactable = true;
        joinRoomPanel.SetActive(true);
        if (!room.isPrivate) ConfirmJoinRoom();
        else { joinPasswordInput.Select(); joinPasswordInput.ActivateInputField(); }
    }
    public void ConfirmJoinRoom()
    {
        if (selectedRoom == null || NetworkClient.active || NetworkServer.active || RoomMigration.Active) return;
        if (selectedRoom.isPrivate && string.IsNullOrWhiteSpace(joinPasswordInput.text)) return;
        var manager = NetManager.Room;
        manager.RoomAuth.ClientKey = RoomAuthenticator.PasswordKey(selectedRoom.isPrivate ? joinPasswordInput.text : "");
        manager.networkAddress = selectedRoom.address;
        if (manager.transport is PortTransport port) port.Port = selectedRoom.port;
        RoomAuthenticator.LastError = null;
        connecting = true;
        joinStatus.text = "Подключение…";
        joinConfirmButton.interactable = false;
        manager.StartClient();
    }
    public void CancelJoinRoom()
    {
        RoomMigration.Cancel();
        connecting = false;
        if (NetworkClient.active) NetManager.Room?.LeaveRoom();
        joinPasswordInput.text = "";
        joinRoomPanel.SetActive(false);
    }
    void UpdateConnectionStatus()
    {
        if (joinRoomPanel == null) return;
        if (RoomMigration.Active)
        {
            joinRoomPanel.SetActive(true);
            joinTitle.text = "Комната";
            joinPasswordInput.gameObject.SetActive(false);
            joinConfirmButton.interactable = false;
            joinStatus.text = RoomMigration.Status;
            return;
        }
        if (connecting && !NetworkClient.active)
        {
            connecting = false;
            joinConfirmButton.interactable = selectedRoom != null;
            joinStatus.text = RoomAuthenticator.LastError ?? "Не удалось подключиться к комнате.";
            RoomAuthenticator.LastError = null;
        }
        else if (!string.IsNullOrEmpty(RoomAuthenticator.LastError) && !NetworkClient.active)
        {
            joinRoomPanel.SetActive(true);
            joinTitle.text = "Комната";
            joinStatus.text = RoomAuthenticator.LastError;
            joinConfirmButton.interactable = selectedRoom != null;
            RoomAuthenticator.LastError = null;
        }
        if (joinRoomPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) CancelJoinRoom();
    }
}
