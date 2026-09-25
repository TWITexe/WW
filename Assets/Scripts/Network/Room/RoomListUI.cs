using System.Collections.Generic;
using Mirror;
using UnityEngine;

// создаёт строки интерфейса из полученного списка комнат.
public class RoomListUI : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private RoomListItem roomItemPrefab;

    private readonly List<RoomListItem> spawnedItems = new();

    // удаляем прежние строки и создаём новые для актуального результата поиска.
    public void ShowRooms(List<RoomInfo> rooms)
    {
        Clear();

        foreach (RoomInfo room in rooms)
        {
            RoomListItem item = Instantiate(roomItemPrefab, contentRoot);
            item.Setup(room, this);
            spawnedItems.Add(item);
        }
    }

    // задаём адрес комнаты и порт транспорта KCP, после чего запускаем клиент.
    public void JoinRoom(RoomInfo room)
    {
        if (NetworkClient.active || NetworkServer.active || RoomMigration.Active) return;
        var menu = FindFirstObjectByType<MainMenuUI>();
        if (menu != null) { menu.JoinRoom(room); return; }
        if (room.isPrivate) return;
        NetworkManager.singleton.networkAddress = room.address;

        if (Transport.active is kcp2k.KcpTransport kcp)
            kcp.Port = room.port;

        NetworkManager.singleton.StartClient();
    }

    // уничтожаем созданные строки и очищаем список ссылок на них.
    private void Clear()
    {
        foreach (RoomListItem item in spawnedItems)
            Destroy(item.gameObject);

        spawnedItems.Clear();
    }
}
