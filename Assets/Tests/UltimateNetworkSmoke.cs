#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;

// Opt-in driver for separate-process verification; excluded from release builds.
public class UltimateNetworkSmoke : MonoBehaviour
{
    string folder;
    float next, flyUntil, steerUntil, walkUntil;
    uint sequence;
    bool floorReady;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] class ActorState
    {
        public uint id;
        public bool local, active, meteor, dead;
        public string kind, motion;
        public int hp, charges, mirrors;
        public float x, y, z, cameraDistance;
        public float charge;
        public double remaining;
    }
    [Serializable] class WorldState { public string kind; public uint owner; public Vector3 position, rollUp; public int placed; public float lastYaw; public bool collapsing; }
    [Serializable] class Snapshot { public bool server; public ActorState[] actors; public string[] effects; public WorldState[] worlds; public string lastCommand, hudCharge; }
    string lastCommand;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args,"--ultimate-test");
        if(index<0||index+1>=args.Length)return;
        var root=new GameObject("Ultimate network probe"); DontDestroyOnLoad(root);
        var probe=root.AddComponent<UltimateNetworkSmoke>();probe.folder=args[index+1];Directory.CreateDirectory(probe.folder);
        Application.logMessageReceived += (message,trace,type) =>
        {
            if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)File.AppendAllText(Path.Combine(probe.folder,"errors.txt"),message+"\n"+trace+"\n");
        };
    }
    PlayerUltimate Local => NetworkClient.localPlayer!=null?NetworkClient.localPlayer.GetComponentInChildren<PlayerUltimate>():null;
    void Update()
    {
        if(Local!=null && steerUntil>Time.unscaledTime) Call(Local,"CmdSteerOrb",Vector3.forward);
        if(Local!=null && (flyUntil>Time.unscaledTime || walkUntil>Time.unscaledTime))
        {
            var movement=Local.GetComponent<RelativeMovement>();movement.enabled=false;
            uint epoch=(uint)Get(movement,"clientEpoch");
            if(epoch!=0)Call(movement,"CmdMove",new RelativeMovement.MoveInput{epoch=epoch,sequence=++sequence,vertical=flyUntil>Time.unscaledTime?1:0,movement=walkUntil>Time.unscaledTime?Vector3.forward:Vector3.zero,forward=Vector3.forward});
        }
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.1f;
        try
        {
            if(Local==null)return;
            if(!floorReady)
            {
                floorReady=true;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Ultimate network test floor";
                floor.transform.position=new Vector3(0,198.75f,0);floor.transform.localScale=new Vector3(60,.5f,60);
            }
            string path=Path.Combine(folder,"command.txt");
            if(File.Exists(path)){string command=File.ReadAllText(path).Trim();File.Delete(path);Execute(command);lastCommand=command;}
            var snapshot=new Snapshot{server=NetworkServer.active,lastCommand=lastCommand,hudCharge=Object.FindFirstObjectByType<UltimateHUD>()?.timer.text,
                actors=Object.FindObjectsByType<PlayerUltimate>(FindObjectsSortMode.None).Select(u=>new ActorState{
                    id=u.netId,local=u.isLocalPlayer,active=u.Active,meteor=u.IsMeteor,kind=u.Kind.ToString(),hp=u.GetComponent<Health>().CurrentHealth,
                    motion=u.presentation.spiritModel.GetComponentInChildren<UltimateSpiritAnimator>(true)?.CurrentMotion,remaining=u.ActiveRemaining,
                    dead=u.GetComponent<Health>().IsDead,charges=u.Charges,x=u.transform.position.x,y=u.transform.position.y,z=u.transform.position.z,charge=u.ChargePercent,
                    cameraDistance=u.isLocalPlayer&&u.ControlledOrb!=null?Vector3.Distance(u.GetComponent<PlayerNetworkCaster>().ViewCamera.transform.position,u.ControlledOrb.PresentationPosition):-1}).OrderBy(u=>u.id).ToArray(),
                worlds=Object.FindObjectsByType<UltimateWorldEffect>(FindObjectsSortMode.None).Select(e=>new WorldState{kind=e.kind.ToString(),owner=e.ownerId,position=e.transform.position,rollUp=e.rotatingVisual!=null?e.rotatingVisual.up:Vector3.up,placed=e.mirrorPoses.Count,lastYaw=e.mirrorPoses.Count>0?e.mirrorPoses[e.mirrorPoses.Count-1].yaw:0,collapsing=e.Collapsing}).ToArray(),
                effects=Object.FindObjectsByType<UltimateWorldEffect>(FindObjectsSortMode.None).Select(e=>e.kind+":"+e.IntactMirrors).ToArray()};
            File.WriteAllText(Path.Combine(folder,"state.json"),JsonUtility.ToJson(snapshot,true));
        }
        catch(Exception error){File.AppendAllText(Path.Combine(folder,"errors.txt"),error+"\n");}
    }
    void Execute(string command)
    {
        string[] args=command.Split(' ');
        if(args[0]=="prepare")
        {
            var remote=NetworkServer.connections.Values.First(c=>c!=NetworkServer.localConnection&&c.identity!=null).identity.GetComponentInChildren<PlayerUltimate>();
            var kind=(UltimateKind)Enum.Parse(typeof(UltimateKind),args[1]);
            foreach(var actor in Object.FindObjectsByType<PlayerUltimate>(FindObjectsSortMode.None))
            {
                actor.ServerEnd(false); actor.GetComponent<Health>().StopAllCoroutines();
                actor.enabled=true;
                Set(actor.GetComponent<Health>(),"isDead",false);Set(actor.GetComponent<Health>(),"currentHealth",actor==remote?100:1000);
                Set(actor.GetComponent<Health>(),"maxHealth",actor==remote?100:1000);Set(actor.GetComponent<Health>(),"shield",0);
                actor.GetComponent<Health>().SetDirty();
                actor.GetComponent<RelativeMovement>().enabled=true;
                actor.GetComponent<RelativeMovement>().ServerTeleport(new Vector3(0,200,actor==remote?0:10),Quaternion.identity);
            }
            var elements=remote.catalog.Get(kind).elements;
            Set(remote.GetComponent<PlayerNetworkCaster>(),"loadout",new ElementLoadout{q=elements[0],e=elements[1],r=elements[2]});
            remote.GetComponent<PlayerNetworkCaster>().SetDirty();
            Set(remote,"chargePoints",UltimateCatalog.FullChargePoints);remote.SetDirty();sequence=0;
        }
        else if(args[0]=="use")
        {
            Local.enabled=true;
            Local.GetComponent<RelativeMovement>().enabled=true;
            Vector3 origin=(Local.ControlledOrb!=null?Local.ControlledOrb.transform.position:Local.transform.position)+Vector3.up;
            Vector3 target=UltimateCatalog.IsGroundTargeted(Local.Kind)?new Vector3(9,199,8):Local.transform.position+Vector3.forward*15;
            Call(Local,"CmdUse",origin,(target-origin).normalized);
        }
        else if(args[0]=="mirror")
        {
            int index=int.Parse(args[1]);Vector3 origin=Local.transform.position+Vector3.up;
            float angle=index*Mathf.PI/3;
            Vector3 target=new Vector3(Mathf.Cos(angle)*8,199,Mathf.Sin(angle)*8);
            Call(Local,"CmdPlaceMirror",origin,(target-origin).normalized,index*15f);
        }
        else if(args[0]=="steer") { Local.enabled=false;steerUntil=Time.unscaledTime+1.5f; }
        else if(args[0]=="jump") Call(Local,"CmdJumpOrb");
        else if(args[0]=="walk") { sequence=(uint)Get(Local.GetComponent<RelativeMovement>(),"clientSequence");walkUntil=Time.unscaledTime+1.1f; }
        else if(args[0]=="swing")
        {
            Vector3 origin=Local.transform.position+Vector3.up;
            Call(Local,"CmdPrimary",origin,Vector3.forward,args[1]=="right");
        }
        else if(args[0]=="shoot")
        {
            var target=Object.FindObjectsByType<PlayerUltimate>(FindObjectsSortMode.None).First(a=>a!=Local);
            var appearance=target.GetComponent<WizardAppearance>();
            var collider=target.GetComponentsInChildren<Collider>().First(c=>appearance.IsDamageCollider(c)&&appearance.IsHeadCollider(c)==(args[1]=="head"));
            Vector3 origin=Local.transform.position+Vector3.up;
            Call(Local,"CmdPrimary",origin,(collider.bounds.center-origin).normalized,false);
        }
        else if(args[0]=="fly") { sequence=0;flyUntil=Time.unscaledTime+1.2f; }
        else if(args[0]=="hurt")
        {
            var remote=NetworkServer.connections.Values.First(c=>c!=NetworkServer.localConnection&&c.identity!=null).identity.GetComponentInChildren<Health>();
            remote.TakeDamage(int.Parse(args[1]),Local.netId);
        }
        else if(args[0]=="charge-reset")
        {
            var remote=NetworkServer.connections.Values.First(c=>c!=NetworkServer.localConnection&&c.identity!=null).identity.GetComponentInChildren<PlayerUltimate>();
            Call(remote,"ConsumeCharge",NetworkTime.time);remote.SetDirty();
            var hp=Local.GetComponent<Health>();Set(hp,"maxHealth",10000);Set(hp,"currentHealth",10000);hp.SetDirty();
        }
        else if(args[0]=="deal")
        {
            var remote=NetworkServer.connections.Values.First(c=>c!=NetworkServer.localConnection&&c.identity!=null).identity.GetComponentInChildren<PlayerUltimate>();
            Local.GetComponent<Health>().TakeDamage(int.Parse(args[1]),remote.netId);
        }
        else if(args[0]=="breakmirror")Object.FindFirstObjectByType<UltimateWorldEffect>().DamageMirror(0,1000);
        else if(args[0]=="wall")
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Polar penetration test wall";
            wall.transform.position=new Vector3(0,201,5);wall.transform.localScale=new Vector3(5,6,.25f);
        }
        else if(args[0]=="capture")ScreenCapture.CaptureScreenshot(Path.Combine(folder,"capture.png"));
        else if(args[0]=="quit")Application.Quit();
    }
    static object Get(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
}
#endif
