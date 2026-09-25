#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEngine;

// Opt-in multi-process integration driver; absent from release players.
public class RoomNetworkSmoke : MonoBehaviour
{
    string folder;
    float next;
    bool connectedEver;
    [Serializable] class Snapshot
    {
        public bool server, connected, connectedEver, migrating, blocked;
        public string playerId, phase, scene, error;
        public int round, players, kills, deaths, votes, goal, minutes;
        public double remaining;
        public MatchStanding[] standings;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args=Environment.GetCommandLineArgs();
        int index=Array.IndexOf(args,"--room-test");
        if(index<0 || index+1>=args.Length)return;
        var root=new GameObject("Room network integration test");DontDestroyOnLoad(root);
        var driver=root.AddComponent<RoomNetworkSmoke>();driver.folder=args[index+1];Directory.CreateDirectory(driver.folder);
        driver.StartCoroutine(driver.Connect(args));
    }
    IEnumerator Connect(string[] args)
    {
        yield return null;
        string Arg(string name,string fallback) { int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback; }
        var manager=NetManager.Room;
        ((PortTransport)manager.transport).Port=ushort.Parse(Arg("--room-port","17991"));
        string password=Arg("--room-password","integration-secret");
        if(Arg("--room-role","client")=="host")
        {
            manager.ConfigureRoom("Network regression",new MatchRules{minutes=30,killGoal=3},password);
            manager.StartHost();
        }
        else
        {
            manager.networkAddress="127.0.0.1";manager.RoomAuth.ClientKey=RoomAuthenticator.PasswordKey(password);manager.StartClient();
        }
    }
    void Update()
    {
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.2f;
        try
        {
            string commandFile=Path.Combine(folder,"command.txt");
            if(File.Exists(commandFile))
            {
                string command=File.ReadAllText(commandFile).Trim();File.Delete(commandFile);Execute(command);
            }
            var manager=NetManager.Room;var state=manager!=null?manager.State:default;
            var stats=NetworkClient.localPlayer!=null?NetworkClient.localPlayer.GetComponentInChildren<PlayerStats>():null;
            connectedEver|=stats!=null;
            var snapshot=new Snapshot{server=NetworkServer.active,connected=stats!=null,connectedEver=connectedEver,migrating=RoomMigration.Active,
                playerId=RoomAuthenticator.PlayerId,phase=state.phase.ToString(),round=state.round,players=state.players,kills=stats!=null?stats.Kills:0,
                deaths=stats!=null?stats.Deaths:0,votes=state.votes,goal=state.rules.killGoal,minutes=state.rules.minutes,blocked=PlayerGameUI.InputBlocked,
                remaining=state.endsAt-NetworkTime.time,standings=state.standings,error=RoomAuthenticator.LastError,
                scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name};
            File.WriteAllText(Path.Combine(folder,"state.json"),JsonUtility.ToJson(snapshot,true));
        }
        catch(Exception e){File.AppendAllText(Path.Combine(folder,"errors.txt"),e+"\n");}
    }
    void Execute(string command)
    {
        var manager=NetManager.Room;
        switch(command)
        {
            case "vote": manager.VoteRematch();break;
            case "kill": NetworkClient.localPlayer.GetComponentInChildren<PlayerStats>().AddKill();break;
            case "remote-kill": NetworkServer.connections.Values.First(c=>c!=NetworkServer.localConnection && c.identity!=null).identity.GetComponentInChildren<PlayerStats>().AddKill();break;
            case "expire": Set(manager,"endsAt",NetworkTime.time-.1);break;
            case "deadline": Set(manager,"rematchAt",NetworkTime.time-.1);break;
            case "leave": manager.LeaveRoom();break;
            case "quit": Application.Quit();break;
            case "damage":
                var health=NetworkClient.localPlayer.GetComponentInChildren<Health>();
                int before=health.CurrentHealth;health.TakeDamage(10);
                File.WriteAllText(Path.Combine(folder,"damage.json"),$"{{\"before\":{before},\"after\":{health.CurrentHealth}}}");break;
        }
    }
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);
}
#endif
