using Mirror;
using UnityEngine;

public class PlayerStats : NetworkBehaviour
{
    [SyncVar] private int kills;
    [SyncVar] private int deaths;
    [SyncVar] private int ping;
    private double nextPing;
    public int Kills => kills;
    public int Deaths => deaths;
    public int Ping => ping;
    public string DisplayName => GetComponentInChildren<PlayerName>(true)?.Nickname ?? "Player";
    [Server] public void AddKill() => kills++;
    [Server] public void AddDeath() => deaths++;
    private void Update()
    {
        if (!isServer || NetworkTime.time < nextPing) return;
        nextPing = NetworkTime.time + 1;
        ping = connectionToClient != null ? Mathf.Clamp((int)(connectionToClient.rtt * 1000), 0, 9999) : 0;
    }
}
