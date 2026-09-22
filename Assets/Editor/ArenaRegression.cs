using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// проверяем пути по реальной коллизии и серверные разрушения отдельно от пользовательского матча.
public static class ArenaRegression
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Run()
    {
        if(Application.isPlaying || NetworkServer.active)throw new Exception("проверка требует остановленного матча");
        CheckRoutes();
        var original=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var tested=new List<ArenaDestructible>();
        var flag=typeof(NetworkServer).GetProperty("active",BindingFlags.Public|BindingFlags.Static);
        Vector3 origin=new Vector3(19000,19000,19000);
        try
        {
            flag.SetValue(null,true);
            ArenaDestructible Create(Vector3 position,int hp,bool server=true)
            {
                var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.transform.position=position;
                var item=obj.AddComponent<ArenaDestructible>();item.durability=hp;
                // в edit mode awake вызываем явно после установки геометрии.
                typeof(ArenaDestructible).GetMethod("Awake",Private).Invoke(item,null);
                var identity=item.GetComponent<NetworkIdentity>();
                typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours",Private).Invoke(identity,null);
                typeof(NetworkIdentity).GetProperty("isServer").SetValue(identity,server);
                if(server){item.OnStartServer();tested.Add(item);}
                return item;
            }
            var front=Create(origin+Vector3.forward*2,45);
            var behind=Create(origin+Vector3.forward*4,20);
            Physics.SyncTransforms();
            ArenaDestructible.Blast(origin,8,24,null);
            Require(!front.Broken&&!behind.Broken,"первый удар или укрытие работают неверно");
            ArenaDestructible.Blast(origin,8,24,null);
            Require(front.Broken&&!behind.Broken,"тот же взрыв прошёл сквозь только что разрушенную стену");
            Require(!front.GetComponent<Collider>().enabled&&!front.GetComponent<Renderer>().enabled,"после разрушения осталось невидимое препятствие");
            ArenaDestructible.Blast(origin,8,24,null);
            Require(behind.Broken,"открытый пролом не пропускает следующий удар");
            var wood=Create(origin+Vector3.right*5,20);
            ArenaDestructible.Hit(wood.GetComponent<Collider>(),origin,20,12);
            Require(wood.Broken,"порыв не разбил лёгкое укрытие");
            // передаём настоящее сериализованное состояние Mirror новой клиентской копии.
            var late=Create(origin+Vector3.left*6,45,false);
            var writer=new NetworkWriter();front.OnSerialize(writer,true);
            late.OnDeserialize(new NetworkReader(writer.ToArraySegment()),true);
            late.OnStartClient();
            Require(late.Broken&&!late.GetComponent<Collider>().enabled,"поздний клиент восстановил разрушенное укрытие");
            var markWall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            markWall.transform.position=origin+Vector3.right*10;markWall.transform.localScale=new Vector3(4,4,1);
            Physics.SyncTransforms();
            SpellSurfaceMark.Place(markWall.transform.position+Vector3.back*.6f,SpellHitKind.Fire,1);
            var mark=markWall.GetComponentInChildren<SpellSurfaceMark>();
            Require(mark!=null&&mark.GetComponent<MeshFilter>().sharedMesh.vertexCount==13,"след не появился на стене");
            Require(mark.GetComponent<Collider>()==null,"след мешает снарядам");
            File.WriteAllText("Logs/arena-mechanics-validation.txt","PASS: all 10 spawns and both terraces connect to courtyard; server damage thresholds; no blast through cover; opened breach; wind destroys wood; Mirror late-join serialization preserves broken state; wall marks have no collider.\n"+DateTime.Now.ToString("O"));
        }
        finally
        {
            foreach(var item in tested){item.OnStopServer();typeof(NetworkIdentity).GetProperty("isServer").SetValue(item.netIdentity,false);}
            flag.SetValue(null,false);
            EditorSceneManager.CloseScene(scene,true);
            SceneManager.SetActiveScene(original);
        }
    }

    // сетка проверяет свободную капсулу и высоту пола: декоративная арка не должна оказаться сплошной стеной.
    static void CheckRoutes()
    {
        const int n=55;
        var height=new float[n,n];var open=new bool[n,n];var visited=new bool[n,n];
        for(int x=0;x<n;x++)for(int z=0;z<n;z++)
        {
            Vector3 point=new Vector3(-54+x*2,16,-54+z*2);
            if(!Physics.Raycast(point,Vector3.down,out var hit,17,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)||hit.normal.y<.75f||hit.point.y>5)continue;
            height[x,z]=hit.point.y;
            open[x,z]=!Physics.CheckCapsule(hit.point+Vector3.up*.6f,hit.point+Vector3.up*1.6f,.49f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        }
        var queue=new Queue<Vector2Int>();queue.Enqueue(new Vector2Int(27,27));visited[27,27]=true;
        var dirs=new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right};
        while(queue.Count>0)
        {
            var a=queue.Dequeue();
            foreach(var dir in dirs)
            {
                var b=a+dir;if(b.x<0||b.y<0||b.x>=n||b.y>=n||visited[b.x,b.y]||!open[b.x,b.y])continue;
                if(Mathf.Abs(height[a.x,a.y]-height[b.x,b.y])>1)continue;
                Vector3 from=new Vector3(-54+a.x*2,height[a.x,a.y]+1,-54+a.y*2);
                Vector3 to=new Vector3(-54+b.x*2,height[b.x,b.y]+1,-54+b.y*2);
                if(Physics.Linecast(from,to,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
                visited[b.x,b.y]=true;queue.Enqueue(b);
            }
        }
        void Reachable(Vector3 point,string name)
        {
            int x=Mathf.RoundToInt((point.x+54)/2),z=Mathf.RoundToInt((point.z+54)/2);
            Require(visited[x,z],"нет пешего маршрута к "+name+" at "+point);
        }
        var map=new System.Text.StringBuilder();
        for(int z=n-1;z>=0;z--){for(int x=0;x<n;x++)map.Append(visited[x,z]?'.':open[x,z]?'o':'#');map.AppendLine();}
        File.WriteAllText("Logs/arena-routes.txt",map.ToString());
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
        foreach(var root in scene.GetRootGameObjects())foreach(var spawn in root.GetComponentsInChildren<NetworkStartPosition>())Reachable(spawn.transform.position,spawn.name);
        Reachable(new Vector3(-30,3,2),"западной галерее");
        Reachable(new Vector3(30,2.4f,-28),"восточному бастиону");
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
