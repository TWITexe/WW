using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// сборщик оставляет обычные редактируемые объекты и префабы; в игре генерация карты не запускается.
public static class RuinedIslandBuilder
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string Folder = "Assets/Arena/RuinedIsland";
    const string Models = "Assets/Other Asstets/LowPolyMedievalStarterPack/Prefabs/";
    static Transform root, architecture, cover, details;
    static Material stone, trim, paving, soil, wood, teal, purple, gold;
    static System.Random random;

    [MenuItem("Tools/Wizard War/Build ruined island")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("сборка арены доступна вне play mode");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        Directory.CreateDirectory("Logs/ArenaBefore");
        if (!File.Exists("Logs/ArenaBefore/SampleScene.unity")) File.Copy(ScenePath, "Logs/ArenaBefore/SampleScene.unity");
        var roots = scene.GetRootGameObjects();
        if (roots.Any(r => r.name == "Ruined Island")) throw new InvalidOperationException("арена уже собрана; редактируйте её объекты вручную");
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        random = new System.Random(1709);
        stone = Mat("Weathered stone", new Color(.43f,.43f,.35f));
        trim = Mat("Carved limestone", new Color(.59f,.57f,.45f));
        paving = Mat("Courtyard slabs", new Color(.38f,.39f,.32f));
        soil = Mat("Old paths", new Color(.36f,.31f,.20f));
        wood = Mat("Old oak", new Color(.34f,.22f,.105f));
        teal = Mat("West turquoise", new Color(.12f,.44f,.43f));
        purple = Mat("East plum", new Color(.40f,.22f,.38f));
        gold = Mat("Keep gold", new Color(.63f,.43f,.12f));
        ResourceMaterial("ArenaDebris", "Universal Render Pipeline/Lit");
        ResourceMaterial("SpellSurfaceMark", "Wizard/Surface Mark");
        root = Group("Ruined Island", null);
        architecture = Group("01 — замковые руины", root);
        cover = Group("02 — разрушаемые укрытия", root);
        details = Group("03 — камни и растительность", root);
        BuildIsland(roots);
        BuildCourtyard();
        BuildKeep();
        BuildTerraces();
        BuildOuterRuins();
        BuildDestructibles();
        BuildScenery();
        SetSpawns(roots);
        foreach (var old in roots)
        {
            if (old.name == "Room" || old.name == "Plane" || old.name.StartsWith("Trap (") || old.name == "SpawnPoint")
                old.SetActive(false);
            if (old.name == "DeadZone")
            {
                old.transform.position = new Vector3(0,-23,0);
                old.transform.localScale = new Vector3(350,8,350);
                foreach (var renderer in old.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            }
        }
        ArchiveOldGeometry(scene);
        // помечаем только неподвижную геометрию; разрушаемые объекты не участвуют в статическом объединении.
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            if (renderer.GetComponentInParent<ArenaDestructible>() == null)
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Isolate(scene, () => { Check(); RenderViews(scene); });
        File.WriteAllText("Logs/arena-build-complete.txt", DateTime.Now.ToString("O"));
    }

    // сохраняем исходные гранёные скалы острова, увеличивая только горизонтальные размеры.
    static void BuildIsland(GameObject[] roots)
    {
        var room = roots.Single(r => r.name == "Room");
        var original = room.transform.Cast<Transform>().Single(t => t.name == "LowPoly");
        var cliffGroup = Group("00 — исходные скалы острова", root);
        foreach (Transform child in original)
        {
            if (!child.name.StartsWith("Clifftile")) continue;
            var copy = Object.Instantiate(child.gameObject, cliffGroup);
            copy.name = child.name;
            copy.transform.SetPositionAndRotation(child.position, child.rotation);
            copy.transform.localScale = child.lossyScale;
        }
        cliffGroup.localScale = new Vector3(1.4f,1,1.4f);
        cliffGroup.position = new Vector3(3.6f,0,0);
        var ground = Group("земля острова — 112 × 116 м", root).gameObject;
        var vertices = new List<Vector3>();
        var submeshes = new List<int>[5];
        for (int i=0;i<5;i++) submeshes[i] = new List<int>();
        for (int x=0;x<28;x++) for (int z=0;z<29;z++)
        {
            float left=-56+x*4, bottom=-58+z*4;
            int start=vertices.Count;
            vertices.Add(new Vector3(left,0,bottom)); vertices.Add(new Vector3(left,0,bottom+4));
            vertices.Add(new Vector3(left+4,0,bottom+4)); vertices.Add(new Vector3(left+4,0,bottom));
            submeshes[random.Next(5)].AddRange(new[]{start,start+1,start+2});
            submeshes[random.Next(5)].AddRange(new[]{start,start+2,start+3});
        }
        var mesh = new Mesh { name="низкополигональная земля", vertices=vertices.ToArray(), subMeshCount=5 };
        for(int i=0;i<5;i++)mesh.SetTriangles(submeshes[i],i);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,Folder+"/IslandGround.asset");
        ground.AddComponent<MeshFilter>().sharedMesh=mesh;
        var colors = new Material[5];
        for(int i=0;i<5;i++)colors[i]=Mat("Moss ground "+i, new Color(.285f+i*.008f,.335f+i*.008f,.205f+i*.006f));
        ground.AddComponent<MeshRenderer>().sharedMaterials=colors;
        ground.AddComponent<MeshCollider>().sharedMesh=mesh;
        // широкие дорожки связывают зоны; тонкие плиты не препятствуют движению контроллера.
        Box("северная дорога",new Vector3(0,.018f,28),new Vector3(7,.03f,54),soil,architecture);
        Box("южная дорога",new Vector3(0,.019f,-31),new Vector3(7,.03f,50),soil,architecture);
        Box("поперечный обход",new Vector3(0,.015f,-18),new Vector3(96,.025f,6),soil,architecture);
        Box("северный обход",new Vector3(0,.016f,24),new Vector3(96,.025f,5),soil,architecture);
    }

    static void BuildCourtyard()
    {
        var courtyard=Group("двор расколотой печати",architecture);
        Box("мощёный двор",new Vector3(0,.055f,1),new Vector3(25,.1f,24),paving,courtyard);
        Model("RuinedArchway",new Vector3(0,0,14),8,0,courtyard);
        Model("RuinedArchway",new Vector3(0,0,-12),7,180,courtyard);
        Model("RuinedArchway",new Vector3(-14,0,1),7,90,courtyard);
        Model("RuinedArchway",new Vector3(14,0,1),6,270,courtyard);
        foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
            Model("RuinedWall",new Vector3(side*10,0,end*10+1),4.2f,side<0?90:270,courtyard);
        var dais=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dais.name="восьмигранная площадка печати"; dais.transform.SetParent(courtyard);
        dais.transform.position=new Vector3(0,.18f,1);dais.transform.localScale=new Vector3(8,.18f,8);
        dais.GetComponent<Renderer>().sharedMaterial=trim;
        Object.DestroyImmediate(dais.GetComponent<Collider>());
        dais.AddComponent<MeshCollider>().sharedMesh=dais.GetComponent<MeshFilter>().sharedMesh;
        // низкие разорванные сегменты дают укрытия, сохраняя четыре выхода из центра.
        foreach(int s in new[]{-1,1})
        {
            Model("StoneWall",new Vector3(s*6,0,1),1.6f,90,courtyard);
            Model("RockCluster",new Vector3(s*8,0,s*6+1),1.15f,s*40,courtyard);
        }
        for(int i=0;i<10;i++)
        {
            float angle=i*Mathf.PI*2/10;
            var slab=Box("сломанная рунная плита",new Vector3(Mathf.Cos(angle)*3,.38f,1+Mathf.Sin(angle)*3),new Vector3(.18f,.035f,.75f),teal,courtyard);
            slab.transform.rotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,0);
        }
    }

    static void BuildKeep()
    {
        var keep=Group("север — руины цитадели",architecture);
        Box("основание цитадели",new Vector3(0,.1f,39),new Vector3(35,.2f,22),paving,keep);
        Model("CastleTower",new Vector3(-17,0,43),13,25,keep);
        Model("CastleTower",new Vector3(17,0,43),10,-20,keep);
        Model("RuinedArchway",new Vector3(0,0,43),10,0,keep);
        Model("RuinedWall",new Vector3(-9.5f,0,45),7,0,keep);
        Model("RuinedWall",new Vector3(10,0,45),5.5f,180,keep);
        Model("RuinedWall",new Vector3(-16,0,32),5.3f,90,keep);
        Model("RuinedWall",new Vector3(16,0,33),4.5f,270,keep);
        Model("StoneWall",new Vector3(-7,0,29),2.2f,0,keep);
        Model("StoneWall",new Vector3(8,0,31),2,20,keep);
        Banner(new Vector3(-11,0,39),gold,keep);
        Banner(new Vector3(11,0,39),gold,keep);
    }

    static void BuildTerraces()
    {
        // каждая высота имеет два пологих подъёма: игрок не попадает в тупик без воздушного рывка.
        Terrace("запад — галерея стражи",new Vector3(-31,0,2),new Vector3(12,3,24),teal);
        Terrace("восток — обвалившийся бастион",new Vector3(31,0,-27),new Vector3(13,2.4f,18),purple);
        var garden=Group("восток — заросший сад",architecture);
        Model("RuinedArchway",new Vector3(32,0,20),6,90,garden);
        Model("RuinedWall",new Vector3(24,0,11),3.8f,25,garden);
        Model("RuinedWall",new Vector3(41,0,30),4.2f,-25,garden);
        Model("MossyBoulder",new Vector3(28,0,32),2.7f,110,garden);
        Model("MossyBoulder",new Vector3(39,0,9),2.6f,40,garden);
    }

    static void Terrace(string name,Vector3 center,Vector3 size,Material flag)
    {
        var group=Group(name,architecture);
        Box("каменная терраса",center+Vector3.up*size.y/2,size,stone,group);
        Box("верхняя плита",center+Vector3.up*(size.y-.08f),new Vector3(size.x+.2f,.16f,size.z+.2f),paving,group);
        foreach(int direction in new[]{-1,1})
        {
            float length=11;
            Ramp("пологий подъём",center+new Vector3(0,0,direction*(size.z/2+length/2)),5.5f,length,size.y,direction>0,group);
            Model("RuinedWall",center+new Vector3(-size.x/2+1,size.y,direction*(size.z/2-2)),3.4f,90,group);
            Model("StoneWall",center+new Vector3(size.x/2-1,size.y,direction*(size.z/2-2)),1.2f,90,group);
        }
        Banner(center+new Vector3(-size.x/2+1,size.y,0),flag,group);
        Model("RockCluster",center+new Vector3(size.x/2+3,0,0),2.1f,20,details);
    }

    static void BuildOuterRuins()
    {
        var perimeter=Group("разорванная внешняя стена",architecture);
        foreach(int side in new[]{-1,1})
        {
            foreach(float z in new[]{-42f,-21f,1f,43f})
                Model("RuinedWall",new Vector3(side*48,0,z),z==1?5:4,90,perimeter);
            foreach(float x in new[]{-35f,-15f,18f,38f})
                Model("RuinedWall",new Vector3(x,0,side*49),4,side<0?180:0,perimeter);
        }
        Model("RuinedArchway",new Vector3(-22,0,-34),6,25,perimeter);
        Model("RuinedWall",new Vector3(-31,0,-35),4,10,perimeter);
        Model("StoneWall",new Vector3(8,0,-34),2.5f,-30,perimeter);
        Model("RuinedWall",new Vector3(-29,0,30),5,100,perimeter);
        Model("StoneWall",new Vector3(-39,0,39),2.7f,40,perimeter);
    }

    static void BuildDestructibles()
    {
        foreach(var position in new[]{new Vector3(-8,0,-21),new Vector3(18,0,21),new Vector3(-40,0,18),new Vector3(40,0,-10),new Vector3(-20,0,38),new Vector3(8,0,21),new Vector3(-17,0,-4),new Vector3(20,0,-7)})
        {
            var crate=Model("WoodenCrate",position,1.5f,random.Next(-25,25),cover);
            Breakable(crate,20,new Color(.37f,.24f,.12f));
            var barrel=Model("WoodenBarrel",position+new Vector3(1.6f,0,.5f),1.4f,0,cover);
            Breakable(barrel,20,new Color(.37f,.24f,.12f));
        }
        // независимые секции баррикад позволяют пробить проход несколькими попаданиями.
        foreach(var position in new[]{new Vector3(-14,0,1),new Vector3(14,0,1),new Vector3(-22,0,-34),new Vector3(32,0,20)})
        {
            var group=Group("разрушаемая каменная баррикада",cover);
            for(int x=0;x<3;x++)for(int y=0;y<2;y++)
            {
                var block=Box("отколотый блок",position+new Vector3((x-1)*1.05f,.43f+y*.84f,0),new Vector3(1,.8f,.85f),trim,group);
                block.transform.rotation=Quaternion.Euler(0,random.Next(-5,6),0);
            }
            Breakable(group.gameObject,45,new Color(.53f,.50f,.40f));
        }
        foreach(var position in new[]{new Vector3(-10,0,-30),new Vector3(23,0,36)})
        {
            var tower=Group("разрушаемый остаток башни",cover);
            for(int level=0;level<4;level++)for(int i=0;i<7;i++)
            {
                if(level==3 && i>3)continue;
                float angle=(i+level*.5f)*Mathf.PI*2/7;
                var block=Box("кладка башни",position+new Vector3(Mathf.Cos(angle)*1.35f,.42f+level*.81f,Mathf.Sin(angle)*1.35f),new Vector3(1.12f,.77f,.7f),level%2==0?stone:trim,tower);
                block.transform.rotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg-90,0);
            }
            Breakable(tower.gameObject,65,new Color(.50f,.48f,.39f));
        }
    }

    static void BuildScenery()
    {
        foreach(var position in new[]{new Vector3(-44,0,-43),new Vector3(43,0,43),new Vector3(-43,0,47),new Vector3(47,0,-43),new Vector3(-22,0,21),new Vector3(22,0,-16),new Vector3(-37,0,-25),new Vector3(9,0,-45),new Vector3(-10,0,24),new Vector3(43,0,21)})
        {
            Model("MossyBoulder",position,2.6f,random.Next(360),details);
            Model("RockCluster",position+new Vector3(2,0,1),1.15f,random.Next(360),details);
        }
        const string tree="Assets/Other Asstets/Low Poly Environment Starter Kit/Prefabs/URP/Trees/Tree 4.prefab";
        foreach(var position in new[]{new Vector3(-52,0,-28),new Vector3(-42,0,30),new Vector3(46,0,38),new Vector3(51,0,14),new Vector3(37,0,49),new Vector3(-35,0,-46)})
            ModelPath(tree,position,random.Next(65,90)*.1f,random.Next(360),details);
        // немного обломков у стен; в основных проходах нет мелких коллайдеров, цепляющих ноги.
        for(int i=0;i<40;i++)
        {
            int side=i%2==0?-1:1;
            var rock=Model("SmallRock",new Vector3(side*random.Next(43,54),0,random.Next(-49,50)),random.Next(4,10)*.1f,random.Next(360),details);
            foreach(var collider in rock.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
        }
    }

    static void SetSpawns(GameObject[] roots)
    {
        var system=roots.Single(r=>r.GetComponent<SpawnManager>()!=null);
        foreach(Transform old in system.transform)old.gameObject.SetActive(false);
        var positions=new[]{new Vector3(-39,0,-43),new Vector3(0,0,-48),new Vector3(39,0,-44),new Vector3(-44,0,-12),new Vector3(45,0,-1),new Vector3(-44,0,24),new Vector3(42,0,33),new Vector3(-29,0,46),new Vector3(28,0,46),new Vector3(0,0,51)};
        var serialized=new SerializedObject(system.GetComponent<SpawnManager>());
        var points=serialized.FindProperty("points");points.arraySize=positions.Length;
        for(int i=0;i<positions.Length;i++)
        {
            var spawn=new GameObject("Island spawn "+(i+1).ToString("00"));
            spawn.transform.SetParent(system.transform);
            spawn.transform.position=positions[i]+Vector3.up*1.5f;
            spawn.transform.rotation=Quaternion.LookRotation(-positions[i].normalized);
            spawn.AddComponent<NetworkStartPosition>();
            points.GetArrayElementAtIndex(i).objectReferenceValue=spawn.transform;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static Transform Group(string name,Transform parent)
    {
        var item=new GameObject(name); item.transform.SetParent(parent,false);return item.transform;
    }
    static Material Mat(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        mat.color=color;mat.SetFloat("_Smoothness",.05f);mat.enableInstancing=true;return mat;
    }
    static void ResourceMaterial(string name,string shader)
    {
        string path="Assets/Resources/"+name+".mat";
        if(AssetDatabase.LoadAssetAtPath<Material>(path)!=null)return;
        var mat=new Material(Shader.Find(shader));mat.enableInstancing=true;AssetDatabase.CreateAsset(mat,path);
    }
    static GameObject Box(string name,Vector3 position,Vector3 scale,Material material,Transform parent)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent);
        obj.transform.position=position;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;return obj;
    }
    static GameObject Model(string name,Vector3 position,float height,float yaw,Transform parent)
        => ModelPath(Models+name+".prefab",position,height,yaw,parent);

    // некоторые исходные префабы содержат сохранённые мировые смещения; нормализуем по видимой геометрии.
    static GameObject ModelPath(string path,Vector3 position,float height,float yaw,Transform parent)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(prefab==null)throw new InvalidOperationException("не найден ассет: "+path);
        var obj=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
        obj.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);obj.transform.localScale=Vector3.one;
        var bounds=BoundsOf(obj);
        obj.transform.localScale=Vector3.one*(height/Mathf.Max(.01f,bounds.size.y));
        obj.transform.rotation=Quaternion.Euler(0,yaw,0);
        bounds=BoundsOf(obj);
        obj.transform.position=position-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        // коллизия повторяет модель арки, а не закрывает её проём одним большим ящиком.
        if(obj.GetComponentsInChildren<Collider>().Length==0)
        {
            var lod=obj.GetComponent<LODGroup>();
            var renderers=lod!=null?lod.GetLODs()[0].renderers:obj.GetComponentsInChildren<Renderer>();
            foreach(var renderer in renderers)
                if(renderer.TryGetComponent<MeshFilter>(out var filter))renderer.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
        }
        return obj;
    }
    static Bounds BoundsOf(GameObject obj)
    {
        var renderers=obj.GetComponentsInChildren<MeshRenderer>();
        if(renderers.Length==0)throw new InvalidOperationException("нет геометрии: "+obj.name);
        var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);return bounds;
    }
    static void Breakable(GameObject obj,int health,Color color)
    {
        var destructible=obj.AddComponent<ArenaDestructible>();destructible.durability=health;destructible.debrisColor=color;
    }
    static void Banner(Vector3 position,Material color,Transform parent)
    {
        Box("древко знамени",position+Vector3.up*2.8f,new Vector3(.12f,5.6f,.12f),wood,parent);
        var cloth=Box("знамя сектора",position+new Vector3(.72f,4.3f,0),new Vector3(1.3f,2,.07f),color,parent);
        Object.DestroyImmediate(cloth.GetComponent<Collider>());
    }
    static void Ramp(string name,Vector3 position,float width,float length,float height,bool highAtStart,Transform parent)
    {
        var obj=new GameObject(name);obj.transform.SetParent(parent);obj.transform.position=position;
        if(!highAtStart)obj.transform.rotation=Quaternion.Euler(0,180,0);
        float x=width/2,z=length/2;
        var mesh=new Mesh {name="пологий каменный подъём",vertices=new[]{new Vector3(-x,0,-z),new Vector3(x,0,-z),new Vector3(-x,height,-z),new Vector3(x,height,-z),new Vector3(-x,0,z),new Vector3(x,0,z)},triangles=new[]{2,4,3,3,4,5,0,2,1,1,2,3,0,4,2,1,3,5,0,1,4,1,5,4}};
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(Folder+"/Ramp.asset"));
        obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=paving;
        obj.AddComponent<MeshCollider>().sharedMesh=mesh;
    }

    [MenuItem("Tools/Wizard War/Check ruined island")]
    public static void Check()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        var objects=scene.GetRootGameObjects();
        var arena=objects.Single(r=>r.name=="Ruined Island");
        Physics.SyncTransforms();
        var spawns=objects.SelectMany(r=>r.GetComponentsInChildren<NetworkStartPosition>()).ToArray();
        if(spawns.Length!=10)throw new Exception("ожидалось 10 новых точек спавна");
        foreach(var spawn in spawns)
        {
            var point=spawn.transform.position;
            if(!Physics.Raycast(point,Vector3.down,out var floor,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))throw new Exception("нет пола под "+spawn.name);
            if(floor.normal.y<.8f || Mathf.Abs(floor.point.y)>.5f)throw new Exception("неровная точка спавна "+spawn.name);
            if(Physics.CheckCapsule(point+Vector3.up*.5f,point+Vector3.up*1.4f,.45f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))throw new Exception("точка спавна занята "+spawn.name);
        }
        var destructibles=arena.GetComponentsInChildren<ArenaDestructible>();
        if(destructibles.Length!=22)throw new Exception("ожидалось 22 разрушаемых объекта");
        var ids=new HashSet<ulong>();
        foreach(var obj in destructibles)
        {
            var identity=obj.GetComponent<NetworkIdentity>();
            if(identity.sceneId==0 || !ids.Add(identity.sceneId))throw new Exception("некорректный сетевой id "+obj.name);
            if(obj.GetComponentsInChildren<Collider>().Length==0)throw new Exception("нет коллизии "+obj.name);
        }
        if(arena.GetComponentsInChildren<Renderer>().Any(r=>r.sharedMaterials.Any(m=>m==null || m.shader==null || m.shader.name.Contains("Error"))))throw new Exception("неверный материал арены");
        File.WriteAllText("Logs/arena-validation.txt", "PASS: 10 safe spawns; 22 destructibles with unique scene IDs and colliders; valid materials.\nRenderers: "+arena.GetComponentsInChildren<Renderer>().Length+"\nColliders: "+arena.GetComponentsInChildren<Collider>().Length);
    }

    // сохраняем обзор и два ракурса с уровня игрока, не меняя игровые камеры.
    static void RenderViews(Scene scene)
    {
        var obj=new GameObject("temporary arena preview");var camera=obj.AddComponent<Camera>();
        camera.farClipPlane=400;camera.nearClipPlane=.1f;camera.fieldOfView=70;
        var canvas=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Canvas>()).ToArray();
        var enabled=canvas.Select(c=>c.enabled).ToArray();foreach(var c in canvas)c.enabled=false;
        try
        {
            camera.orthographic=true;camera.orthographicSize=76;
            bool fog=RenderSettings.fog;
            try { RenderSettings.fog=false; Render(camera,new Vector3(105,105,-118),new Vector3(0,0,1),"Logs/arena-overview.png"); }
            finally {RenderSettings.fog=fog;}
            camera.orthographic=false;
            Render(camera,new Vector3(0,2.3f,-24),new Vector3(0,3,15),"Logs/arena-courtyard.png");
            Render(camera,new Vector3(-31,5.1f,3),new Vector3(9,3,8),"Logs/arena-gallery.png");
        }
        finally {for(int i=0;i<canvas.Length;i++)canvas[i].enabled=enabled[i];Object.DestroyImmediate(obj);}
    }

    // другие открытые сцены временно выключаются только для проверки и снимка, затем полностью восстанавливаются.
    public static void Isolate(Scene scene,Action action)
    {
        var disabled=new List<GameObject>();
        for(int i=0;i<SceneManager.sceneCount;i++)
        {
            var other=SceneManager.GetSceneAt(i);
            if(other==scene || !other.isLoaded)continue;
            foreach(var obj in other.GetRootGameObjects())if(obj.activeSelf){obj.SetActive(false);disabled.Add(obj);}
        }
        try {Physics.SyncTransforms();action();}
        finally {foreach(var obj in disabled)if(obj!=null)obj.SetActive(true);Physics.SyncTransforms();}
    }

    [MenuItem("Tools/Wizard War/Polish ruined island")]
    public static void Polish()
    {
        if(Application.isPlaying)throw new Exception("нужно выйти из play mode");
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        root=scene.GetRootGameObjects().Single(r=>r.name=="Ruined Island").transform;
        ArchiveOldGeometry(scene);
        RefineCliffs();
        RefineRamps();
        // капсула стандартного цилиндра при плоском масштабе становится выше модели; заменяем точной сеткой.
        var dais=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="восьмигранная площадка печати");
        foreach(var collider in dais.GetComponents<Collider>())if(!(collider is MeshCollider))Object.DestroyImmediate(collider);
        if(dais.GetComponent<MeshCollider>()==null)dais.gameObject.AddComponent<MeshCollider>().sharedMesh=dais.GetComponent<MeshFilter>().sharedMesh;
        // коллайдеры остаются только у первого lod: переключение графики не требует дублирующей физики.
        foreach(var lod in root.GetComponentsInChildren<LODGroup>())
        {
            var levels=lod.GetLODs();
            if(levels.Length<2)continue;
            var retained=new HashSet<Renderer>(levels[0].renderers);
            for(int i=1;i<levels.Length;i++)foreach(var renderer in levels[i].renderers)
                if(renderer!=null&&!retained.Contains(renderer))foreach(var collider in renderer.GetComponents<Collider>())Object.DestroyImmediate(collider);
        }
        if(root.Find("04 — разбитая брусчатка")==null)
        {
            var tiles=Group("04 — разбитая брусчатка",root);
            random=new System.Random(209);
            var materials=new[]{Mat("Path slab light",new Color(.44f,.43f,.33f)),Mat("Path slab dark",new Color(.32f,.33f,.27f))};
            for(int i=0;i<150;i++)
            {
                float x,z;
                if(i<70){x=(float)random.NextDouble()*5-2.5f;z=(float)random.NextDouble()*96-48;}
                else{x=(float)random.NextDouble()*88-44;z=(i%2==0?-18:24)+(float)random.NextDouble()*3-1.5f;}
                if(Mathf.Abs(x)<13&&z>-12&&z<14)continue;
                var tile=Box("старая плита",new Vector3(x,.047f,z),new Vector3(.6f+(float)random.NextDouble(),.045f,.65f+(float)random.NextDouble()),materials[i%2],tiles);
                tile.transform.rotation=Quaternion.Euler(0,random.Next(-18,19),0);
                Object.DestroyImmediate(tile.GetComponent<Collider>());
                GameObjectUtility.SetStaticEditorFlags(tile,StaticEditorFlags.BatchingStatic);
            }
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        Isolate(scene,()=>{Check();ArenaRegression.Run();RenderViews(scene);});
        // арена была загружена дополнительно для работы; не оставляем её поверх меню перед запуском игры.
        var menu=SceneManager.GetSceneByPath("Assets/Scenes/Menu.unity");
        if(menu.isLoaded){SceneManager.SetActiveScene(menu);EditorSceneManager.CloseScene(scene,true);}
        File.WriteAllText("Logs/arena-polish-complete.txt",DateTime.Now.ToString("O"));
    }

    // mirror включает корневые сетевые объекты при старте, поэтому старые ловушки прячем под отключённого родителя.
    static void ArchiveOldGeometry(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        var archive=roots.FirstOrDefault(r=>r.name=="Previous island — disabled");
        if(archive==null)archive=Group("Previous island — disabled",null).gameObject;
        foreach(var obj in roots)
            if(obj.name=="Room"||obj.name=="Plane"||obj.name.StartsWith("Trap (")||obj.name=="SpawnPoint")obj.transform.SetParent(archive.transform,true);
        archive.SetActive(false);
    }

    // заменяем только верхние грани скал материалом земли, сохраняя исходную геометрию и каменную палитру боков.
    static void RefineCliffs()
    {
        var grass=Mat("Cliff moss",new Color(.29f,.34f,.21f));
        foreach(var filter in root.Find("00 — исходные скалы острова").GetComponentsInChildren<MeshFilter>())
        {
            if(AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(Folder+"/Cliff_"))continue;
            var original=filter.sharedMesh;
            var mesh=Object.Instantiate(original);mesh.name=original.name+" moss top";
            var top=new List<int>();var vertices=original.vertices;
            int count=original.subMeshCount;mesh.subMeshCount=count+1;
            for(int sub=0;sub<count;sub++)
            {
                var triangles=original.GetTriangles(sub);var sides=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
                    bool upper=Vector3.Cross(b-a,c-a).normalized.y>.45f&&(a.y+b.y+c.y)/3>original.bounds.max.y-2.5f;
                    var list=upper?top:sides;list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);
                }
                mesh.SetTriangles(sides,sub);
            }
            mesh.SetTriangles(top,count);
            AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(Folder+"/Cliff_"+original.name+".asset"));
            filter.sharedMesh=mesh;
            var renderer=filter.GetComponent<MeshRenderer>();var materials=renderer.sharedMaterials.ToList();materials.Add(grass);renderer.sharedMaterials=materials.ToArray();
        }
    }

    // отдельные вершины граней убирают сглаженные чёрные швы на низкополигональных подъёмах.
    static void RefineRamps()
    {
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if(filter.name!="пологий подъём"||filter.sharedMesh.vertexCount>6)continue;
            var mesh=filter.sharedMesh;var original=mesh.vertices;var indices=mesh.triangles;
            var vertices=new Vector3[indices.Length];var triangles=new int[indices.Length];
            for(int i=0;i<indices.Length;i++){vertices[i]=original[indices[i]];triangles[i]=i;}
            mesh.Clear();mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            filter.GetComponent<MeshCollider>().sharedMesh=mesh;
        }
    }
    static void Render(Camera camera,Vector3 position,Vector3 target,string path)
    {
        camera.transform.position=position;camera.transform.LookAt(target);
        var rt=new RenderTexture(1600,1000,24);var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally {camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
}
