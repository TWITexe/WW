using System;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.Events;

// пустой запрос поиска: дл€ получени€ объ€влени€ комнаты дополнительные данные не требуютс€.
[Serializable]
public class RoomDiscoveryRequest : NetworkMessage
{
}

// содержит сетевое объ€вление комнаты и локально добавленный адрес отправител€.
[Serializable]
public class RoomDiscoveryResponse : NetworkMessage
{
    public long serverId;
    public Uri uri;

    public string roomName;
    public int players;
    public int maxPlayers;

    // адрес отправител€ не передаЄтс€ этим полем по сети, а заполн€етс€ на клиенте из ответа транспорта.
    public IPEndPoint EndPoint { get; set; }
}

// позвол€ет назначать обработчики найденной комнаты через событи€ Unity.
[Serializable]
public class RoomFoundUnityEvent : UnityEvent<RoomDiscoveryResponse>
{
}

// обмениваетс€ объ€влени€ми комнат через механизм поиска Mirror в локальной сети.
public class RoomNetworkDiscovery : NetworkDiscoveryBase<RoomDiscoveryRequest, RoomDiscoveryResponse>
{
    [Header("Room Info")]
    [SerializeField] private string roomName = "Local Room";
    [SerializeField] private int maxPlayers = 8;

    public RoomFoundUnityEvent OnRoomFound = new RoomFoundUnityEvent();

    // создаЄм запрос без дополнительных полей.
    protected override RoomDiscoveryRequest GetRequest()
    {
        return new RoomDiscoveryRequest();
    }

    // сервер отвечает своим адресом, именем комнаты и текущим числом подключений.
    protected override RoomDiscoveryResponse ProcessRequest(RoomDiscoveryRequest request, IPEndPoint endpoint)
    {
        try
        {
            return new RoomDiscoveryResponse
            {
                serverId = ServerId,
                uri = transport.ServerUri(),

                roomName = roomName,
                players = NetworkServer.connections.Count,
                maxPlayers = maxPlayers
            };
        }
        catch (NotImplementedException)
        {
            Debug.LogError($"Transport {transport} не поддерживает Network Discovery");
            throw;
        }
    }

    // подставл€ем реальный адрес отправител€ вместо служебного адреса сервера и публикуем результат.
    protected override void ProcessResponse(RoomDiscoveryResponse response, IPEndPoint endpoint)
    {
        response.EndPoint = endpoint;

        // важно дл€ подключени€ к найденной комнате:
        // сервер может прислать uri с localhost/0.0.0.0,
        // поэтому подставл€ем реальный IP, с которого пришЄл ответ
        UriBuilder realUri = new UriBuilder(response.uri)
        {
            Host = endpoint.Address.ToString()
        };

        response.uri = realUri.Uri;

        OnRoomFound.Invoke(response);
    }

    // задаЄм им€, которое сервер будет возвращать при поиске комнат.
    public void SetRoomName(string newRoomName)
    {
        roomName = string.IsNullOrWhiteSpace(newRoomName) ? "Local Room" : newRoomName;
    }
}