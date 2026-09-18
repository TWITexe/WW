using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

// собирает объ€влени€ комнат в локальной сети и передаЄт их интерфейсу через общий договор.
public class LanRoomProvider : MonoBehaviour, IRoomProvider
{
    [SerializeField] private RoomNetworkDiscovery discovery;

    public event Action<List<RoomInfo>> RoomsUpdated;

    private readonly Dictionary<long, RoomInfo> discoveredRooms = new();

    // провер€ем компонент поиска и подписываемс€ на найденные комнаты.
    private void Awake()
    {
        if (discovery == null)
        {
            Debug.LogError("RoomNetworkDiscovery не назначен в инспекторе!");
            enabled = false;
            return;
        }

        discovery.OnRoomFound.AddListener(OnRoomFound);
    }

    // снимаем подписку, чтобы уничтоженный источник не обрабатывал ответы поиска.
    private void OnDestroy()
    {
        if (discovery != null)
            discovery.OnRoomFound.RemoveListener(OnRoomFound);
    }

    // очищаем накопленные комнаты и запускаем поиск заново.
    public void RefreshRooms()
    {
        discoveredRooms.Clear();

        // запускаем поиск комнат в локальной сети
        discovery.StartDiscovery();
    }

    // обновл€ем запись по идентификатору сервера и отправл€ем интерфейсу актуальную копию списка.
    private void OnRoomFound(RoomDiscoveryResponse response)
    {
        Uri uri = response.uri;

        ushort port = 7777;

        if (uri.Port > 0)
            port = (ushort)uri.Port;

        RoomInfo roomInfo = new RoomInfo(
            response.roomName,
            uri.Host,
            port,
            response.players,
            response.maxPlayers
        );

        discoveredRooms[response.serverId] = roomInfo;

        RoomsUpdated?.Invoke(new List<RoomInfo>(discoveredRooms.Values));
    }
}