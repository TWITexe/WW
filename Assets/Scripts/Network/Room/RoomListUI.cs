using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class RoomListUI : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private RoomListItem roomItemPrefab;

    private readonly List<RoomListItem> spawnedItems = new();

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

    public void JoinRoom(RoomInfo room)
    {
        NetworkManager.singleton.networkAddress = room.address;

        if (Transport.active is kcp2k.KcpTransport kcp)
            kcp.Port = room.port;

        NetworkManager.singleton.StartClient();
    }

    private void Clear()
    {
        foreach (RoomListItem item in spawnedItems)
            Destroy(item.gameObject);

        spawnedItems.Clear();
    }
}