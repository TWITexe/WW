using System;
using System.Collections;
using UnityEngine;

public class DedicatedServerBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--ww-dedicated") < 0) return;
        var go = new GameObject("Dedicated server bootstrap"); DontDestroyOnLoad(go); go.AddComponent<DedicatedServerBootstrap>();
    }
    IEnumerator Start()
    {
        while (NetManager.Room == null) yield return null;
        var room = NetManager.Room;
        room.ConfigureRoom("Wizard War · сервер", MatchRules.Default, Environment.GetEnvironmentVariable("WW_ROOM_PASSWORD") ?? "");
        room.StartServer();
        room.GetComponent<RoomNetworkDiscovery>()?.AdvertiseServer();
        Destroy(gameObject);
    }
}
