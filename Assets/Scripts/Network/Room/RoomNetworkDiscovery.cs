using System;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.Events;

// пустой запрос поиска: для получения объявления комнаты дополнительные данные не требуются.
[Serializable]
public class RoomDiscoveryRequest : NetworkMessage
{
}

// содержит сетевое объявление комнаты и локально добавленный адрес отправителя.
[Serializable]
public class RoomDiscoveryResponse : NetworkMessage
{
    public long serverId;
    public Uri uri;

    public string roomName;
    public int players;
    public int maxPlayers;
    public bool isPrivate;
    public MatchRules rules;

    // адрес отправителя не передаётся этим полем по сети, а заполняется на клиенте из ответа транспорта.
    public IPEndPoint EndPoint { get; set; }
}

// позволяет назначать обработчики найденной комнаты через события Unity.
[Serializable]
public class RoomFoundUnityEvent : UnityEvent<RoomDiscoveryResponse>
{
}

// обменивается объявлениями комнат через механизм поиска Mirror в локальной сети.
public class RoomNetworkDiscovery : NetworkDiscoveryBase<RoomDiscoveryRequest, RoomDiscoveryResponse>
{
    long roomServerId;
    float nextAdvertiseAttempt;
    bool advertisingRequested;

    private void Awake() => roomServerId = RandomLong();

    // Mirror's default Start advertises every headless process, including test clients.
    public override void Start()
    {
        if (transport == null) transport = Transport.active;
        if (NetworkServer.active) AdvertiseServer();
    }
    public new void AdvertiseServer()
    {
        if (!NetworkServer.active) return;
        advertisingRequested = true;
        TryAdvertise();
    }
    void TryAdvertise()
    {
        nextAdvertiseAttempt = Time.unscaledTime + 2;
        try { base.AdvertiseServer(); }
        catch (System.Net.Sockets.SocketException)
        {
            // Another local instance may release the discovery port shortly after handoff.
            // Gameplay and the reconnect coroutine must remain operational in the meantime.
        }
    }
    void Update()
    {
        if (!NetworkServer.active && advertisingRequested)
        {
            advertisingRequested = false;
            StopDiscovery();
        }
        else if (NetworkServer.active && advertisingRequested && serverUdpClient == null && Time.unscaledTime >= nextAdvertiseAttempt)
            TryAdvertise();
    }
    [Header("Room Info")]
    [SerializeField] private string roomName = "Local Room";
    [SerializeField] private int maxPlayers = 8;

    public RoomFoundUnityEvent OnRoomFound = new RoomFoundUnityEvent();

    // создаём запрос без дополнительных полей.
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
                serverId = roomServerId,
                uri = transport.ServerUri(),

                roomName = roomName,
                players = NetworkServer.connections.Count,
                maxPlayers = NetworkManager.singleton != null ? NetworkManager.singleton.maxConnections : maxPlayers,
                isPrivate = NetManager.Room != null && !string.IsNullOrEmpty(NetManager.Room.RoomAuth.ServerKey),
                rules = NetManager.Room != null ? NetManager.Room.Rules : MatchRules.Default
            };
        }
        catch (NotImplementedException)
        {
            Debug.LogError($"Transport {transport} не поддерживает Network Discovery");
            throw;
        }
    }

    // подставляем реальный адрес отправителя вместо служебного адреса сервера и публикуем результат.
    protected override void ProcessResponse(RoomDiscoveryResponse response, IPEndPoint endpoint)
    {
        response.EndPoint = endpoint;

        // важно для подключения к найденной комнате:
        // сервер может прислать uri с localhost/0.0.0.0,
        // поэтому подставляем реальный IP, с которого пришёл ответ
        UriBuilder realUri = new UriBuilder(response.uri)
        {
            Host = endpoint.Address.ToString()
        };

        response.uri = realUri.Uri;

        OnRoomFound.Invoke(response);
    }

    // задаём имя, которое сервер будет возвращать при поиске комнат.
    public void SetRoomName(string newRoomName)
    {
        roomName = string.IsNullOrWhiteSpace(newRoomName) ? "Local Room" : newRoomName;
    }
}
