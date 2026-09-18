using Mirror;
using UnityEngine;

// создаёт сетевого персонажа при подключении игрока к серверу.
public class NetManager : NetworkManager
{
    // выбираем стартовую точку, создаём префаб и связываем его с подключением клиента.
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Transform startPos = GetStartPosition();

        GameObject player = startPos != null
            ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
            : Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);

        // поднимаем игрока над землёй перед сетевым появлением.
        player.transform.position += Vector3.up * 2;

        // связываем персонажа с подключением и показываем его участникам.
        NetworkServer.AddPlayerForConnection(conn, player);
    }
}