using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;

public struct EconomyMatchMessage : NetworkMessage
{
    public string matchId, playerId;
    public bool eligible, settled;
    public int reward;
}
[Serializable]
public struct EconomyRoundSnapshot
{
    public string matchId;
    public int peakPlayers;
    public string[] players, forfeited;
    public double[] playedSeconds;
    public EconomyMatchMessage[] results;
}

public partial class NetManager
{
    string economyRoundId;
    int economyPeakPlayers;
    readonly Dictionary<string, double> economyJoined = new();
    readonly HashSet<string> economyForfeited = new();
    readonly Dictionary<string, EconomyMatchMessage> economyResults = new();
    public EconomyMatchMessage EconomyState { get; private set; }
    void BeginEconomyRound()
    {
        economyRoundId = Guid.NewGuid().ToString("N");
        economyJoined.Clear(); economyForfeited.Clear(); economyResults.Clear(); economyPeakPlayers = 0;
        EconomyState = new EconomyMatchMessage { matchId = economyRoundId };
        // Players already connected at round start include scene-loading time;
        // otherwise a complete one-minute round can be truncated to 59 seconds.
        foreach (var conn in NetworkServer.connections.Values.Where(c => c.isAuthenticated)) JoinEconomy(conn);
    }
    void JoinEconomy(NetworkConnectionToClient conn)
    {
        if (ServerPhase == MatchPhase.Results) { SendEconomyResult(conn); return; }
        if (ServerPhase != MatchPhase.Playing || string.IsNullOrEmpty(economyRoundId)) return;
        string id = Id(conn);
        if (!string.IsNullOrEmpty(id) && !economyJoined.ContainsKey(id)) economyJoined[id] = NetworkTime.time;
        economyPeakPlayers = Math.Max(economyPeakPlayers, Participants().Count());
        conn.Send(new EconomyMatchMessage { matchId = economyRoundId, playerId = id });
    }
    void DisconnectEconomy(NetworkConnectionToClient conn)
    {
        if (ServerPhase == MatchPhase.Playing) economyForfeited.Add(Id(conn));
        RoomAuth.Forget(conn);
    }
    void CompleteEconomyRound()
    {
        for (int i = 0; i < finalStandings.Length; i++)
        {
            var player = finalStandings[i];
            bool eligible = economyPeakPlayers >= 2 && economyJoined.ContainsKey(player.playerId) && !economyForfeited.Contains(player.playerId);
            int seconds = eligible ? (int)Math.Max(0, Math.Floor(Math.Min(NetworkTime.time, endsAt) - economyJoined[player.playerId])) : 0;
            economyResults[player.playerId] = new EconomyMatchMessage
            {
                matchId = economyRoundId, playerId = player.playerId, eligible = eligible, settled = true,
                reward = eligible ? ShopCatalog.Reward(player.kills, seconds, economyPeakPlayers, i == 0) : 0
            };
        }
        foreach (var conn in Participants()) SendEconomyResult(conn);
    }
    void SendEconomyResult(NetworkConnectionToClient conn)
    {
        if (economyResults.TryGetValue(Id(conn), out var result)) conn.Send(result);
    }
    void RegisterEconomyClient()
    {
        EconomyState = default;
        NetworkClient.RegisterHandler<EconomyMatchMessage>(message =>
        {
            EconomyState = message;
            EconomyClient.Instance?.Award(message);
        });
    }
    EconomyRoundSnapshot CaptureEconomy()
    {
        var players = economyJoined.Keys.ToArray();
        return new EconomyRoundSnapshot
        {
            matchId = economyRoundId, peakPlayers = economyPeakPlayers, players = players,
            playedSeconds = players.Select(id => Math.Max(0, Math.Min(NetworkTime.time, endsAt) - economyJoined[id])).ToArray(),
            forfeited = economyForfeited.ToArray(), results = economyResults.Values.ToArray()
        };
    }
    void RestoreEconomy(EconomyRoundSnapshot snapshot)
    {
        economyRoundId = snapshot.matchId ?? Guid.NewGuid().ToString("N");
        economyPeakPlayers = snapshot.peakPlayers;
        economyJoined.Clear(); economyForfeited.Clear(); economyResults.Clear();
        if (snapshot.players != null && snapshot.playedSeconds != null)
            for (int i = 0; i < Math.Min(snapshot.players.Length, snapshot.playedSeconds.Length); i++)
                economyJoined[snapshot.players[i]] = NetworkTime.time - snapshot.playedSeconds[i];
        foreach (string id in snapshot.forfeited ?? Array.Empty<string>()) economyForfeited.Add(id);
        foreach (var result in snapshot.results ?? Array.Empty<EconomyMatchMessage>()) economyResults[result.playerId] = result;
    }
}

