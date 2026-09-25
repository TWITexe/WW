#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using UnityEngine;

// Explicit development-only multi-process test. Never active during ordinary gameplay.
public class ShopNetworkSmoke : MonoBehaviour
{
    string folder, role;
    float next;
    [Serializable] class Snapshot
    {
        public bool connected, server, trusted, settled, ownsHat, renderedHat;
        public string phase, error, playerId, color, staffTint, robeTint;
        public string[] visibleColors;
        public int coins, reward, players, visiblePaidHats;
        public ElementLoadout loadout;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--shop-test");
        if(i<0||i+1>=args.Length)return;
        var go=new GameObject("Shop network smoke");DontDestroyOnLoad(go);
        var driver=go.AddComponent<ShopNetworkSmoke>();driver.folder=args[i+1];Directory.CreateDirectory(driver.folder);
        driver.role=Array.IndexOf(args,"--shop-server")>=0?"server":Array.IndexOf(args,"--shop-host")>=0?"host":"client";driver.StartCoroutine(driver.Connect());
    }
    IEnumerator Connect()
    {
        while(NetManager.Room==null)yield return null;
        var manager=NetManager.Room;((PortTransport)manager.transport).Port=17993;
        if(role=="server")
        {
            manager.ConfigureRoom("Shop regression",new MatchRules{minutes=2,killGoal=3},"shop-test");manager.StartServer();
        }
        else
        {
            while(EconomyClient.Instance?.Profile==null)yield return null;
            LocalPlayerSettings.Instance.CosmeticSettings.preferredColor=PlayerColorId.Blue;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--shop-buy")>=0)
            {
                EconomyClient.Instance.Purchase("hat_ember");while(EconomyClient.Instance.Busy)yield return null;
                EconomyClient.Instance.Equip("hat","hat_ember");while(EconomyClient.Instance.Busy)yield return null;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--shop-color-buy")>=0)
            {
                EconomyClient.Instance.Purchase("color_yellow");
                LocalPlayerSettings.Instance.CosmeticSettings.preferredColor=PlayerColorId.Yellow;
            }
            if(role=="host")
            {
                manager.ConfigureRoom("Local W regression",new MatchRules{minutes=2,killGoal=3},"shop-test");manager.StartHost();
            }
            else { manager.networkAddress="127.0.0.1";manager.RoomAuth.ClientKey=RoomAuthenticator.PasswordKey("shop-test");manager.StartClient(); }
        }
    }
    void Update()
    {
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;
        try
        {
            string command=Path.Combine(folder,"command.txt");
            if(File.Exists(command))
            {
                string value=File.ReadAllText(command).Trim();File.Delete(command);
                if(value=="kill"&&NetworkServer.active)NetworkServer.connections.Values.Where(c=>c.identity!=null).OrderBy(c=>c.connectionId).First().identity.GetComponentInChildren<PlayerStats>().AddKill();
                if(value=="quit")Application.Quit();
                if(value=="refresh")EconomyClient.Instance?.Refresh();
                if(value=="forged-color"&&playerForCommand()!=null)
                    typeof(PlayerColor).GetMethod("CmdRequestColor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                        .Invoke(playerForCommand().GetComponentInChildren<PlayerColor>(),new object[]{PlayerColorId.Purple});
            }
            var manager=NetManager.Room;var account=EconomyClient.Instance?.Profile;
            var player=NetworkClient.localPlayer;var wizard=player!=null?player.GetComponentInChildren<WizardAppearance>():null;
            var state=new Snapshot {server=NetworkServer.active,connected=player!=null,trusted=false,
                coins=account?.coins??-1,reward=account?.lastReward??-1,ownsHat=account!=null&&account.Owns("hat_ember"),
                renderedHat=wizard!=null&&wizard.visualRoot.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("hat_ember")),
                visiblePaidHats=UnityEngine.Object.FindObjectsByType<WizardCosmetics>(FindObjectsSortMode.None).Count(w=>w.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("hat_ember"))),
                color=player!=null?player.GetComponentInChildren<PlayerColor>().DisplayColor.ToString():"",
                visibleColors=UnityEngine.Object.FindObjectsByType<PlayerColor>(FindObjectsSortMode.None).Select(p=>p.DisplayColor.ToString()).OrderBy(x=>x).ToArray(),
                staffTint=Tint(wizard,"Staff_Separate",0),robeTint=Tint(wizard,"Body_Robe",-1),
                playerId=account?.playerId,phase=manager!=null?(NetworkServer.active?manager.ServerPhase:manager.State.phase).ToString():"",error=RoomAuthenticator.LastError,
                settled=manager!=null&&manager.EconomyState.settled,players=NetworkServer.active?NetworkServer.connections.Count:manager!=null?manager.State.players:0,
                loadout=player!=null?player.GetComponentInChildren<PlayerNetworkCaster>().Loadout:default};
            File.WriteAllText(Path.Combine(folder,"state.json"),JsonUtility.ToJson(state,true));
        }
        catch(Exception e){File.AppendAllText(Path.Combine(folder,"errors.txt"),e+"\n");}
    }
    static NetworkIdentity playerForCommand()=>NetworkClient.localPlayer;
    static string Tint(WizardAppearance wizard,string name,int index)
    {
        if(wizard==null)return "";
        var renderer=wizard.visualRoot.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.name==name);
        if(renderer==null)return "";
        var block=new MaterialPropertyBlock();if(index<0)renderer.GetPropertyBlock(block);else renderer.GetPropertyBlock(block,index);
        return block.GetColor("_BaseColor").ToString();
    }
}
#endif
