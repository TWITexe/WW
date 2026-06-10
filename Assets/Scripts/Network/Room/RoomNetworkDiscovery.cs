using System;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class RoomDiscoveryRequest : NetworkMessage
{
}

[Serializable]
public class RoomDiscoveryResponse : NetworkMessage
{
    public long serverId;
    public Uri uri;

    public string roomName;
    public int players;
    public int maxPlayers;

    // Это не отправляется по сети, мы заполняем это уже на клиенте
    public IPEndPoint EndPoint { get; set; }
}

[Serializable]
public class RoomFoundUnityEvent : UnityEvent<RoomDiscoveryResponse>
{
}

public class RoomNetworkDiscovery : NetworkDiscoveryBase<RoomDiscoveryRequest, RoomDiscoveryResponse>
{
    [Header("Room Info")]
    [SerializeField] private string roomName = "Local Room";
    [SerializeField] private int maxPlayers = 8;

    public RoomFoundUnityEvent OnRoomFound = new RoomFoundUnityEvent();

    protected override RoomDiscoveryRequest GetRequest()
    {
        return new RoomDiscoveryRequest();
    }

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

    protected override void ProcessResponse(RoomDiscoveryResponse response, IPEndPoint endpoint)
    {
        response.EndPoint = endpoint;

        // Важно:
        // сервер может прислать uri с localhost/0.0.0.0,
        // поэтому подставляем реальный IP, с которого пришёл ответ
        UriBuilder realUri = new UriBuilder(response.uri)
        {
            Host = endpoint.Address.ToString()
        };

        response.uri = realUri.Uri;

        OnRoomFound.Invoke(response);
    }

    public void SetRoomName(string newRoomName)
    {
        roomName = string.IsNullOrWhiteSpace(newRoomName) ? "Local Room" : newRoomName;
    }
}