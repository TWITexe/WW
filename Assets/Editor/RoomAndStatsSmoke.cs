using System;
using System.Net;
using System.Reflection;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// проверяет создание комнаты, её объявление и имя игрока на локальном хосте в отдельном редакторе.
[InitializeOnLoad]
public static class RoomAndStatsSmoke
{
    const string Flag="RoomAndStatsSmoke";
    const BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Instance;
    static double started,next;
    static int stage,checks;
    static RoomNetworkDiscovery discovery;
    // подключаем сценарий к обновлению редактора с управлением через флаг сеанса.
    static RoomAndStatsSmoke(){EditorApplication.update+=Tick;}
    // подготавливаем визуальные настройки и диалог комнаты перед переходом в игровой режим.
    public static void Run()
    {
        SpellReadabilityTuning.Apply();RoomCreationUIBuilder.Apply();
        SessionState.SetBool(Flag,true);EditorApplication.isPlaying=true;
    }
    // учитываем выполненное условие или завершаем проверку исключением.
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    // проверяем пустое и допустимое имя комнаты, ответ поиска и отображаемое имя игрока.
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(started==0){started=now;next=now+2;}
            if(now-started>75)throw new Exception("Room test timeout");
            if(now<next)return;
            if(stage==0)
            {
                LocalPlayerSettings.Instance.SetNickname("Тестовый маг");
                var menu=UnityEngine.Object.FindFirstObjectByType<MainMenuUI>();
                discovery=(RoomNetworkDiscovery)typeof(MainMenuUI).GetField("discovery",Fields).GetValue(menu);
                menu.CreateRoom();
                var panel=(GameObject)typeof(MainMenuUI).GetField("createRoomPanel",Fields).GetValue(menu);
                var input=(TMP_InputField)typeof(MainMenuUI).GetField("roomNameInput",Fields).GetValue(menu);
                Check(panel.activeInHierarchy&&!NetworkServer.active,"CreateRoom must open dialog before starting host");
                input.text="   ";menu.ConfirmCreateRoom();Check(!NetworkServer.active,"Empty name must not start host");
                menu.CancelCreateRoom();Check(!panel.activeSelf,"Cancel closes dialog");menu.CreateRoom();
                input.text="  Арена 42  ";
                if(NetworkManager.singleton.transport is PortTransport port)port.Port=17988;
                menu.ConfirmCreateRoom();Check(NetworkServer.active,"Valid room starts host");
                stage=1;next=now+2;
            }
            else
            {
                if(NetworkClient.localPlayer==null){next=now+.2;return;}
                var stats=NetworkClient.localPlayer.GetComponentInChildren<PlayerStats>();
                Check(stats.DisplayName=="Тестовый маг","TAB name must match child PlayerName");
                var response=(RoomDiscoveryResponse)typeof(RoomNetworkDiscovery).GetMethod("ProcessRequest",Fields).Invoke(discovery,new object[]{new RoomDiscoveryRequest(),new IPEndPoint(IPAddress.Loopback,17988)});
                Check(response.roomName=="Арена 42","Discovery must advertise trimmed chosen room name");
                Check(PlayerPrefs.GetString("LastRoomName")=="Арена 42","Room name remembered");
                foreach(string path in new[]{"Assets/Prefabs/FireBall.prefab","Assets/Prefabs/WindFlow.prefab","Assets/Other Asstets/GeneratedWizard/WaterBolt.prefab"})
                {var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);Check(prefab.GetComponent<ElementalVisual>().projectileVisualScale==2.5f,"Projectile visual scale must be 2.5");Check(prefab.GetComponent<Collider>()!=null,"Projectile hitbox retained");}
                Debug.Log("ROOM_STATS_SMOKE_PASSED: "+checks+" checks");SessionState.SetBool(Flag,false);NetworkManager.singleton.StopHost();EditorApplication.Exit(0);
            }
        }
        catch(Exception e){Debug.LogException(e);SessionState.SetBool(Flag,false);EditorApplication.Exit(1);}
    }
}
