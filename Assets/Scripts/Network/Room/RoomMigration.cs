using System;
using System.Collections;
using System.Linq;
using Mirror;
using UnityEngine;

[Serializable]
public struct RoomPeer
{
    public string playerId;
    public string address;
}
public struct RoomMigrationMessage : NetworkMessage
{
    public string roomName;
    public string passwordKey;
    public ushort port;
    public MatchStateMessage match;
    public double remaining;
    public double rematchRemaining;
    public MatchStanding[] scores;
    public RoomPeer[] peers;
    public string[] votes;
    public bool handoff;
    public EconomyRoundSnapshot economy;
}

public partial class NetManager
{
    RoomMigrationMessage? lastMigration;
    RoomMigrationMessage? migrationRestore;
    double migrationGraceUntil;
    void RegisterMigrationClient()
    {
        lastMigration = null;
        NetworkClient.RegisterHandler<RoomMigrationMessage>(message =>
        {
            lastMigration = message;
            if (message.handoff && !NetworkServer.active && !Leaving) RoomMigration.Begin(message);
        });
    }
    void BroadcastMigration(bool handoff = false)
    {
        if (!NetworkServer.active) return;
        var peers = Participants().Where(c => c != NetworkServer.localConnection).OrderBy(c => c.connectionId).ToArray();
        if (peers.Length == 0) return;
        var message = new RoomMigrationMessage
        {
            roomName = RoomName, passwordKey = RoomAuth.ServerKey,
            port = transport is PortTransport portTransport ? portTransport.Port : (ushort)7777,
            match = new MatchStateMessage { rules = Rules, phase = ServerPhase, round = round, reason = finishReason, standings = finalStandings },
            remaining = Math.Max(0, endsAt - NetworkTime.time), rematchRemaining = Math.Max(0, rematchAt - NetworkTime.time),
            scores = Standings(), votes = rematchVotes.ToArray(), handoff = handoff, economy = CaptureEconomy(),
            peers = peers.Select(c => new RoomPeer { playerId = Id(c), address = c.address }).ToArray()
        };
        foreach (var peer in peers) peer.Send(message);
    }
    void PrepareHostHandoff() => BroadcastMigration(true);
    public override void OnClientDisconnect()
    {
        // Voluntary exits and rejected passwords must never create a new room.
        if (!Leaving && !NetworkServer.active && lastMigration.HasValue && !RoomMigration.Active)
            RoomMigration.Begin(lastMigration.Value);
        base.OnClientDisconnect();
    }
    public override void OnApplicationQuit()
    {
        Leaving = true;
        // Peers also keep periodic snapshots for window close / process loss.
        PrepareHostHandoff();
        base.OnApplicationQuit();
    }
    public void PrepareMigration(RoomMigrationMessage message)
    {
        ConfigureRoom(message.roomName, message.match.rules, "");
        RoomAuth.ServerKey = RoomAuth.ClientKey = message.passwordKey;
        migrationRestore = message;
    }
    void RestoreMigratedRound()
    {
        var message = migrationRestore.Value;
        round = message.match.round; ServerPhase = message.match.phase;
        endsAt = NetworkTime.time + message.remaining;
        rematchAt = NetworkTime.time + message.rematchRemaining;
        finalStandings = message.match.standings ?? Array.Empty<MatchStanding>();
        finishReason = message.match.reason;
        rematchVotes.Clear();
        foreach (string id in message.votes ?? Array.Empty<string>()) rematchVotes.Add(id);
        // Allow existing participants to reconnect before deciding that everyone voted.
        migrationGraceUntil = NetworkTime.time + 10;
        restarting = false;
        RestoreEconomy(message.economy);
    }
    void RestoreMigratedPlayer(GameObject player, NetworkConnectionToClient conn)
    {
        if (!migrationRestore.HasValue) return;
        var saved = migrationRestore.Value.scores?.FirstOrDefault(p => p.playerId == Id(conn)) ?? default;
        player.GetComponentInChildren<PlayerStats>()?.RestoreScore(saved.kills, saved.deaths);
    }
}

// Lives across the menu scene while Mirror disposes of the old manager.
public class RoomMigration : MonoBehaviour
{
    public static bool Active { get; private set; }
    public static string Status { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { Active = false; Status = null; }
    public static void Cancel()
    {
        if (!Active) return;
        // Set Leaving before cancelling so disconnection cannot start another migration.
        NetManager.Room?.LeaveRoom();
        var runner = FindFirstObjectByType<RoomMigration>();
        if (runner != null) Destroy(runner.gameObject);
        Active = false; Status = null;
    }
    private void OnDestroy() { Active = false; Status = null; }
    public static void Begin(RoomMigrationMessage message)
    {
        if (Active || message.peers == null || message.peers.Length == 0) return;
        if (!message.peers.Any(p => p.playerId == RoomAuthenticator.PlayerId)) return;
        Active = true;
        var root = new GameObject("Room host handoff");
        DontDestroyOnLoad(root);
        root.AddComponent<RoomMigration>().StartCoroutine(Transfer(message));
    }
    static IEnumerator Transfer(RoomMigrationMessage message)
    {
        Status = "Передача комнаты новому хосту…";
        yield return null;
        if (NetworkClient.active) NetworkManager.singleton.StopClient();
        // Disconnect cleanup completes after its callback returns, and then loads Menu.
        yield return new WaitForSecondsRealtime(1);
        float deadline = Time.realtimeSinceStartup + 35;
        bool becomeHost = message.peers[0].playerId == RoomAuthenticator.PlayerId;
        NetManager manager = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            manager = NetManager.Room;
            if (manager != null && !NetworkClient.active && !NetworkServer.active &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == manager.offlineScene) break;
            yield return null;
        }
        if (manager != null && !NetworkClient.active && !NetworkServer.active)
        {
            if (manager.transport is PortTransport port) port.Port = message.port;
            if (becomeHost)
            {
                manager.PrepareMigration(message);
                manager.StartHost();
                manager.GetComponent<RoomNetworkDiscovery>()?.AdvertiseServer();
            }
            else
            {
                yield return new WaitForSecondsRealtime(2);
                while (Time.realtimeSinceStartup < deadline)
                {
                    manager = NetManager.Room;
                    if (manager != null && !NetworkClient.active)
                    {
                        manager.RoomAuth.ClientKey = message.passwordKey;
                        manager.networkAddress = message.peers[0].address;
                        manager.StartClient();
                    }
                    yield return new WaitForSecondsRealtime(1);
                    if (NetworkClient.localPlayer != null) break;
                }
            }
            while (NetworkClient.localPlayer == null && Time.realtimeSinceStartup < deadline) yield return null;
        }
        if (NetworkClient.localPlayer == null)
        {
            RoomAuthenticator.LastError = "Не удалось подключиться к новому хосту.";
            NetManager.Room?.LeaveRoom();
        }
        Active = false; Status = null;
        var runner = FindFirstObjectByType<RoomMigration>();
        if (runner != null) Destroy(runner.gameObject);
    }
}
