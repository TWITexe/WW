using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

public partial class NetManager : NetworkManager
{
    public MatchRules Rules { get; private set; } = MatchRules.Default;
    public string RoomName { get; private set; } = "Комната";
    public RoomAuthenticator RoomAuth => (RoomAuthenticator)authenticator;
    public MatchStateMessage State { get; private set; }
    public MatchPhase ServerPhase { get; private set; }
    public static NetManager Room => singleton as NetManager;
    public static bool CombatAllowed => Room == null || !NetworkServer.active ||
        (Room.ServerPhase == MatchPhase.Playing && NetworkTime.time < Room.endsAt);
    readonly HashSet<string> rematchVotes = new();
    MatchStanding[] finalStandings = Array.Empty<MatchStanding>();
    int round;
    double endsAt, rematchAt, nextBroadcast;
    string finishReason = "";
    bool restarting;
    public bool Leaving { get; private set; }
    public override void Awake()
    {
        base.Awake();
        if (singleton != this) return;
        authenticator = GetComponent<RoomAuthenticator>() ?? gameObject.AddComponent<RoomAuthenticator>();
    }
    public void ConfigureRoom(string roomName, MatchRules rules, string password)
    {
        RoomName = roomName; Rules = rules.Validated();
        RoomAuth.ServerKey = RoomAuthenticator.PasswordKey(password); RoomAuth.ClientKey = RoomAuth.ServerKey;
        GetComponent<RoomNetworkDiscovery>()?.SetRoomName(roomName);
    }
    public override void OnStartServer()
    {
        base.OnStartServer(); Leaving = false; ServerPhase = MatchPhase.Waiting; restarting = false;
        NetworkServer.RegisterHandler<RematchVoteMessage>(OnRematchVote);
        if (migrationRestore == null) { round = 0; rematchVotes.Clear(); }
    }
    public override void OnStartClient()
    {
        base.OnStartClient(); Leaving = false; State = default;
        NetworkClient.RegisterHandler<MatchStateMessage>(message => State = message);
        NetworkClient.RegisterHandler<RoomExitMessage>(message => { RoomAuthenticator.LastError = message.reason; LeaveRoom(); });
        RegisterMigrationClient();
        RegisterEconomyClient();
    }
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        if (sceneName == onlineScene) BeginRound();
    }
    void BeginRound()
    {
        if (migrationRestore != null) { RestoreMigratedRound(); return; }
        round++; ServerPhase = MatchPhase.Playing; endsAt = NetworkTime.time + Rules.minutes * 60;
        rematchVotes.Clear(); finalStandings = Array.Empty<MatchStanding>(); finishReason = ""; restarting = false;
        BeginEconomyRound();
        BroadcastState();
    }
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Transform start = GetStartPosition();
        var player = Instantiate(playerPrefab, (start != null ? start.position : Vector3.zero) + Vector3.up * 2,
            start != null ? start.rotation : Quaternion.identity);
        RestoreMigratedPlayer(player, conn);
        NetworkServer.AddPlayerForConnection(conn, player);
        if (migrationRestore.HasValue && Participants().Count() >= migrationRestore.Value.peers.Length)
            migrationGraceUntil = 0;
        if (ServerPhase == MatchPhase.Waiting && !NetworkServer.isLoadingScene) BeginRound();
        JoinEconomy(conn);
        BroadcastState();
    }
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (conn.authenticationData is string id) rematchVotes.Remove(id);
        DisconnectEconomy(conn);
        base.OnServerDisconnect(conn); nextBroadcast = 0;
    }
    public override void Update()
    {
        base.Update();
        if (!NetworkServer.active || NetworkServer.isLoadingScene || Leaving) return;
        if (ServerPhase == MatchPhase.Playing && NetworkTime.time >= endsAt) FinishRound("Время вышло");
        if (ServerPhase == MatchPhase.Results && !restarting && NetworkTime.time >= migrationGraceUntil)
        {
            var players = Participants().ToList();
            if (players.Count > 0 && (NetworkTime.time >= rematchAt || players.All(c => rematchVotes.Contains(Id(c)))))
                StartCoroutine(RestartRound());
        }
        if (NetworkTime.time >= nextBroadcast)
        {
            nextBroadcast = NetworkTime.time + .5; BroadcastState(); BroadcastMigration();
        }
    }
    IEnumerable<NetworkConnectionToClient> Participants() => NetworkServer.connections.Values.Where(c => c.isAuthenticated && c.identity != null);
    static string Id(NetworkConnectionToClient conn) => conn.authenticationData as string ?? "";
    public MatchStanding[] Standings() => Participants().Select(c =>
    {
        var stats = c.identity.GetComponentInChildren<PlayerStats>();
        return new MatchStanding { playerId = Id(c), name = stats.DisplayName, color = stats.DisplayColor, kills = stats.Kills, deaths = stats.Deaths };
    }).OrderByDescending(p => p.kills).ThenBy(p => p.deaths).ThenBy(p => p.playerId, StringComparer.Ordinal).ToArray();
    public void CheckKillGoal(PlayerStats scorer)
    {
        if (NetworkServer.active && ServerPhase == MatchPhase.Playing && scorer.Kills >= Rules.killGoal) FinishRound("Достигнута цель по убийствам");
    }
    void FinishRound(string reason)
    {
        if (ServerPhase != MatchPhase.Playing) return;
        ServerPhase = MatchPhase.Results; finishReason = reason; rematchAt = NetworkTime.time + 60;
        finalStandings = Standings(); rematchVotes.Clear(); CompleteEconomyRound();
        BroadcastState(); BroadcastMigration();
    }
    void OnRematchVote(NetworkConnectionToClient conn, RematchVoteMessage message)
    {
        if (ServerPhase != MatchPhase.Results || restarting || message.round != round || conn.identity == null || NetworkTime.time >= rematchAt) return;
        rematchVotes.Add(Id(conn)); BroadcastState(); BroadcastMigration();
    }
    public void VoteRematch()
    {
        if (NetworkClient.isConnected && State.phase == MatchPhase.Results) NetworkClient.Send(new RematchVoteMessage { round = State.round });
    }
    void BroadcastState()
    {
        if (!NetworkServer.active) return;
        var players = Participants().ToList();
        foreach (var conn in players)
        {
            conn.Send(new MatchStateMessage { rules = Rules, phase = ServerPhase, round = round,
                endsAt = endsAt, rematchAt = rematchAt, reason = finishReason, standings = finalStandings,
                players = players.Count, votes = players.Count(c => rematchVotes.Contains(Id(c))), voted = rematchVotes.Contains(Id(conn)) });
            if (ServerPhase == MatchPhase.Results) SendEconomyResult(conn);
        }
    }
    IEnumerator RestartRound()
    {
        restarting = true;
        var excluded = Participants().Where(c => !rematchVotes.Contains(Id(c))).ToArray();
        foreach (var conn in excluded.Where(c => c != NetworkServer.localConnection)) conn.Send(new RoomExitMessage { reason = "Реванш начался без вас." });
        yield return new WaitForSecondsRealtime(.5f);
        foreach (var conn in excluded) if (conn != NetworkServer.localConnection) conn.Disconnect();
        if (NetworkServer.localConnection != null && !rematchVotes.Contains(Id(NetworkServer.localConnection))) { LeaveRoom(); yield break; }
        if (!Participants().Any(c => rematchVotes.Contains(Id(c)))) { LeaveRoom(); yield break; }
        ServerPhase = MatchPhase.Waiting; migrationRestore = null; ServerChangeScene(onlineScene);
    }
    public void LeaveRoom()
    {
        if (Leaving) return;
        Leaving = true; PrepareHostHandoff(); StartCoroutine(StopRoom());
    }
    IEnumerator StopRoom()
    {
        if (NetworkServer.active) yield return new WaitForSecondsRealtime(.3f);
        if (NetworkServer.active && NetworkClient.active) StopHost();
        else if (NetworkClient.active) StopClient();
        else if (NetworkServer.active) StopServer();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    public override void OnStopServer()
    {
        GetComponent<RoomNetworkDiscovery>()?.StopDiscovery();
        NetworkServer.UnregisterHandler<RematchVoteMessage>(); ServerPhase = MatchPhase.Waiting; base.OnStopServer();
    }
}
