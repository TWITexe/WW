using Mirror;
using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private RoomNetworkDiscovery discovery;

    public void CreateRoom()
    {
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