using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Mirror;

public class NetManager : NetworkManager
{

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Transform startPos = GetStartPosition();
        GameObject player = startPos != null
           ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
           : Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);

        // Поднимаем игрока над землёй
        player.transform.position += Vector3.up * 2;

        // Спавним игрока для всех
        NetworkServer.AddPlayerForConnection(conn, player);
    }
}
