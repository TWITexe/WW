using Mirror;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private RoomNetworkDiscovery discovery;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Button confirmRoomButton;

    private void Start()
    {
        if (roomNameInput != null)
        {
            roomNameInput.characterLimit = 32;
            roomNameInput.onValueChanged.AddListener(UpdateRoomButton);
            roomNameInput.onSubmit.AddListener(_ => ConfirmCreateRoom());
        }
    }

    private void UpdateRoomButton(string value)
    {
        if (confirmRoomButton != null) confirmRoomButton.interactable = !string.IsNullOrWhiteSpace(value);
    }

    public void CreateRoom()
    {
        if (NetworkClient.active || NetworkServer.active) return;
        if (createRoomPanel == null || roomNameInput == null)
        {
            Debug.LogError("Assign the saved Create Room dialog in MainMenuUI.");
            return;
        }
        createRoomPanel.SetActive(true);
        string nickname = LocalPlayerSettings.Instance != null ? LocalPlayerSettings.Instance.CosmeticSettings.nickname : "Player";
        roomNameInput.text = PlayerPrefs.GetString("LastRoomName", "Комната " + nickname);
        UpdateRoomButton(roomNameInput.text);
        roomNameInput.Select();
        roomNameInput.ActivateInputField();
    }

    public void CancelCreateRoom()
    {
        if (createRoomPanel != null) createRoomPanel.SetActive(false);
    }

    public void ConfirmCreateRoom()
    {
        if (createRoomPanel == null || !createRoomPanel.activeInHierarchy || roomNameInput == null || NetworkClient.active || NetworkServer.active) return;
        string name = roomNameInput.text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Length > 32) name = name.Substring(0, 32);
        if (discovery == null || NetworkManager.singleton == null)
        {
            Debug.LogError("Room creation requires NetworkManager and RoomNetworkDiscovery.");
            return;
        }
        discovery.SetRoomName(name);
        PlayerPrefs.SetString("LastRoomName", name);
        PlayerPrefs.Save();
        createRoomPanel.SetActive(false);
        NetworkManager.singleton.StartHost();

        if (discovery != null)
            discovery.AdvertiseServer();
    }

    public void ConnectToServer()
    {
        NetworkManager.singleton.StartClient();
    }

    public void CreateServer()
    {
        NetworkManager.singleton.StartServer();

        if (discovery != null)
            discovery.AdvertiseServer();
    }

    public void CloseWindow(GameObject window)
    {
        window.SetActive(false);
    }
    public void OpenWindow(GameObject window)
    {
        window.SetActive(true);
    }
}
