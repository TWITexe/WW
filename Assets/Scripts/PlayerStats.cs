using Mirror;
using UnityEngine;
using System.Collections.Generic;

// синхронизирует убийства, смерти и пинг для таблицы игроков.
public class PlayerStats : NetworkBehaviour
{
    private static readonly HashSet<PlayerStats> clientPlayers = new HashSet<PlayerStats>();
    public static IEnumerable<PlayerStats> ClientPlayers => clientPlayers;
    private PlayerName playerName;
    private PlayerColor playerColor;
    public Color DisplayColor => playerColor != null ? playerColor.DisplayColor : Color.white;

    // сбрасываем реестр и при запуске без перезагрузки домена редактора.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => clientPlayers.Clear();

    // получаем ссылку на имя один раз, а не при каждом обновлении таблицы.
    private void Awake()
    {
        playerName = GetComponentInChildren<PlayerName>(true);
        playerColor = GetComponentInChildren<PlayerColor>(true);
    }

    // сетевые обратные вызовы поддерживают список без поиска по всей сцене.
    public override void OnStartClient()
    {
        base.OnStartClient();
        clientPlayers.Add(this);
    }

    // удаляем отключённого игрока из таблицы сразу после завершения клиентской копии.
    public override void OnStopClient()
    {
        clientPlayers.Remove(this);
        base.OnStopClient();
    }
    [SyncVar] private int kills;
    [SyncVar] private int deaths;
    [SyncVar] private int ping;
    private double nextPing;
    public int Kills => kills;
    public int Deaths => deaths;
    public int Ping => ping;
    public string DisplayName => playerName != null ? playerName.Nickname : "Player";
    // сервер увеличивает счётчик убийств, который затем получат клиенты.
    [Server] public void AddKill() => kills++;
    // сервер увеличивает счётчик смертей, который затем получат клиенты.
    [Server] public void AddDeath() => deaths++;
    // раз в секунду обновляем пинг по времени кругового обмена с клиентом, переводя секунды в миллисекунды.
    private void Update()
    {
        if (!isServer || NetworkTime.time < nextPing) return;
        nextPing = NetworkTime.time + 1;
        ping = connectionToClient != null ? Mathf.Clamp((int)(connectionToClient.rtt * 1000), 0, 9999) : 0;
    }
}
