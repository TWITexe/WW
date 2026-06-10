using System.Collections.Generic;
using UnityEngine;

public class RoomBrowserController : MonoBehaviour
{
    [SerializeField] private MonoBehaviour providerBehaviour;
    [SerializeField] private RoomListUI roomListUI;

    private IRoomProvider roomProvider;

    private void Awake()
    {
        roomProvider = providerBehaviour as IRoomProvider;

        if (roomProvider == null)
        {
            Debug.LogError("Provider должен реализовывать IRoomProvider");
            return;
        }

        roomProvider.RoomsUpdated += OnRoomsUpdated;
    }

    private void OnDestroy()
    {
        if (roomProvider != null)
            roomProvider.RoomsUpdated -= OnRoomsUpdated;
    }

    public void Refresh()
    {
        roomProvider.RefreshRooms();
    }

    private void OnRoomsUpdated(List<RoomInfo> rooms)
    {
        roomListUI.ShowRooms(rooms);
    }
}