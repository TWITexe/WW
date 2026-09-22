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
using UnityEngine.UI;
using Object = UnityEngine.Object;

// явный установщик сохраняет ассеты, сетевые префабы и карточки, не меняя расстановку сцены.
public static class AdvancedSpellBuilder
{
    const string Folder = "Assets/Spells/Advanced";
    const string Pack = "Assets/Hovl Studio/Magic effects pack/Prefabs/";
    static readonly string[] Ids = { "ShardVortex", "ScaldingMist", "Meteor", "ThermalSpring", "BoilingIce", "IceBridge", "CrystalCrash", "SteamLens" };
    static readonly string[] Titles = { "Осколочная воронка", "Обжигающий туман", "Метеорит", "Термальный источник", "Кипящий лёд", "Ледяной мост", "Кристальный обвал", "Паровая линза" };
    static readonly int[][] Recipes = { new[]{0,1,2}, new[]{0,1,4}, new[]{3,0,2}, new[]{0,2,4}, new[]{2,0,4}, new[]{2,1,4}, new[]{4,1,2}, new[]{2,1,0} };
    static readonly float[] Cooldowns = { 13, 12, 20, 17, 12, 18, 18, 12 };
    static readonly string[] Descriptions = {
        "Воронка замедляет на 35%. Через 1,5 с осколок наносит 20 урона по области и замедляет на 35% на 2 с. Радиус 3 м.",
        "Снаряд создаёт оранжевый туман радиусом 2,3 м на 3 с: 5 урона каждые 0,5 с. Дым закрывает обзор всем.",
        "Через 2,5 с метеорит наносит 45 урона в радиусе 3 м. Земля горит 3 с: 5 урона в секунду. Во время подготовки скорость снижена на 50%.",
        "Источник на 5 с лечит владельца на 10 здоровья в секунду в радиусе 2,5 м. Получение урона прекращает лечение этого источника.",
        "Ледяной снаряд при столкновении создаёт область радиусом 2,5 м на 5 с: 7 урона каждую секунду.",
        "Скользкая платформа 3 × 10 м перед магом на 10 с. Может висеть в воздухе. Требует свободного места; доступна всем игрокам.",
        "Через 2,5 с кристалл падает в отмеченную область: 30 урона и оглушение на 1,5 с. Радиус 2,5 м.",
        "Линза перед магом на 4 с. Один собственный фаерболл или ледяное копьё, прошедший через неё, получает +20% скорости и +4 базового урона. После усиления линза раскалывается." };

    [MenuItem("Tools/Wizard War/Install advanced spells")]
    public static void Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before installing spells.");
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var spells = new List<AdvancedSpell>();
        for (int i = 0; i < Ids.Length; i++)
        {
            string path = Folder + "/" + Ids[i] + ".asset";
            Backup(path);
            var spell = AssetDatabase.LoadAssetAtPath<AdvancedSpell>(path);
            bool created = spell == null;
            if (created) { spell = ScriptableObject.CreateInstance<AdvancedSpell>(); AssetDatabase.CreateAsset(spell, path); }
            if (created)
            {
                var data = new SerializedObject(spell);
                data.FindProperty("displayName").stringValue = Titles[i];
                data.FindProperty("description").stringValue = Descriptions[i];
                data.FindProperty("cooldown").floatValue = Cooldowns[i];
                var recipe = data.FindProperty("recipe"); recipe.arraySize = 3;
                for (int j = 0; j < 3; j++) recipe.GetArrayElementAtIndex(j).enumValueIndex = Recipes[i][j];
                data.ApplyModifiedPropertiesWithoutUndo();
                Configure(spell, i);
            }
            // существующие префабы сохраняем вместе с ручной настройкой пользователя.
            if (created || spell.effectPrefab == null) spell.effectPrefab = BuildEffect(spell, Ids[i]);
            EditorUtility.SetDirty(spell);
            spells.Add(spell);
        }
        var existingSeal=AssetDatabase.LoadAssetAtPath<TacticalSpell>("Assets/Scripts/Spells/Tactical/FireSeal.asset");
        if(existingSeal.Recipe[1]!=MagicElement.Ice)RecolorSeal();
        const string playerPath = "Assets/Prefabs/Player.prefab";
        Backup(playerPath);
        var player = PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            var data = new SerializedObject(player.GetComponentInChildren<SpellManager>(true));
            foreach (var spell in spells) AddReference(data.FindProperty("spells"), spell);
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(player, playerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        RestoreIdentity(playerPath);
        foreach (string path in new[]{"Assets/Prefabs/UI/SpellbookUI.prefab", "Assets/Prefabs/UI/MatchUI.prefab"})
        {
            if (!File.Exists(path)) continue;
            Backup(path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try { UpdateRoot(root, spells); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Scene original = SceneManager.GetActiveScene();
        foreach (string path in new[]{"Assets/Scenes/Menu.unity", "Assets/Scenes/SampleScene.unity"})
        {
            Backup(path);
            Scene scene = SceneManager.GetSceneByPath(path);
            bool loaded = scene.IsValid() && scene.isLoaded;
            if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects()) UpdateRoot(root, spells);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.IsValid()) SceneManager.SetActiveScene(original);
        AssetDatabase.SaveAssets();
        Validate();
    }

    // значения задаются только при первом создании, последующая ручная настройка сохраняется.
    static void Configure(AdvancedSpell spell, int index)
    {
        spell.kind = (AdvancedSpellKind)index;
        spell.tint = new Color(.4f,.8f,1);
        switch (spell.kind)
        {
            case AdvancedSpellKind.ShardVortex: spell.delay=1.5f; spell.duration=.6f; spell.impactDamage=20; spell.slow=.65f; spell.tint=new Color(.9f,.22f,.2f); break;
            case AdvancedSpellKind.ScaldingMist: spell.duration=3; spell.radius=2.3f; spell.tickInterval=.5f; spell.tickDamage=5; spell.tint=new Color(1,.38f,.08f); break;
            case AdvancedSpellKind.Meteor: spell.delay=2.5f; spell.duration=3; spell.impactDamage=45; spell.tickDamage=5; spell.tint=new Color(1,.35f,.08f); break;
            case AdvancedSpellKind.ThermalSpring: spell.duration=5; spell.radius=2.5f; spell.healPerTick=10; spell.tint=new Color(.3f,1,.75f); break;
            case AdvancedSpellKind.BoilingIce: spell.duration=5; spell.radius=2.5f; spell.tickDamage=7; break;
            case AdvancedSpellKind.IceBridge: spell.duration=10; break;
            case AdvancedSpellKind.CrystalCrash: spell.delay=2.5f; spell.duration=.6f; spell.radius=2.5f; spell.impactDamage=30; spell.stunDuration=1.5f; break;
            case AdvancedSpellKind.SteamLens: spell.duration=4; spell.radius=1; break;
        }
    }

    static GameObject BuildEffect(AdvancedSpell spell, string id)
    {
        string path = Folder + "/" + id + ".prefab";
        Backup(path);
        var root = new GameObject(id);
        try
        {
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<NetworkTransformReliable>().syncDirection = SyncDirection.ServerToClient;
            var body = root.AddComponent<Rigidbody>(); body.isKinematic=true; body.useGravity=false;
            var effect = root.AddComponent<AdvancedSpellEffect>(); effect.definition=spell;
            var visual = root.AddComponent<AdvancedSpellVisual>(); visual.effect=effect;
            var material = Solid(id, spell.tint);
            if (spell.IsProjectile)
            {
                var sphere = root.AddComponent<SphereCollider>(); sphere.isTrigger=true; sphere.radius=.18f;
                visual.projectile = Mesh(root.transform, "Projectile", PrimitiveType.Sphere, Vector3.zero, Vector3.one*.35f, material);
                Particles(visual.projectile.transform, "Trail", spell.tint, .15f, 18, .2f);
            }
            if (spell.HasWarning)
            {
                visual.warning = new GameObject("Warning"); visual.warning.transform.SetParent(root.transform,false);
                Ring(visual.warning.transform, spell.radius, material);
                if (spell.kind == AdvancedSpellKind.ShardVortex)
                {
                    var ps = Particles(visual.warning.transform,"Slowing vortex",spell.tint,spell.radius,35,.1f);
                    var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.orbitalY=2; velocity.radial=-1;
                }
                visual.fallingBody=Mesh(root.transform,"Falling body",spell.kind==AdvancedSpellKind.Meteor?PrimitiveType.Sphere:PrimitiveType.Cube,Vector3.up*12,
                    spell.kind==AdvancedSpellKind.Meteor?Vector3.one*1.8f:new Vector3(.7f,2.2f,.7f),material);
                if(spell.kind!=AdvancedSpellKind.Meteor)
                {
                    visual.fallingBody.GetComponent<MeshFilter>().sharedMesh=CrystalMesh();
                    visual.fallingBody.transform.localScale=new Vector3(.8f,1.4f,.8f);
                }
                else
                {
                    visual.fallingBody.GetComponent<Renderer>().sharedMaterial=Solid("MeteorRock",new Color(.17f,.12f,.1f));
                    // светящиеся каменные пластины читаются как раскалённые трещины на тёмном ядре.
                    for(int k=0;k<9;k++)
                    {
                        float angle=k*2.4f;Vector3 normal=new Vector3(Mathf.Cos(angle),Mathf.Sin(k*1.7f)*.7f,Mathf.Sin(angle)).normalized;
                        var crack=Mesh(visual.fallingBody.transform,"Ember crack",PrimitiveType.Cube,normal*.49f,new Vector3(.035f,.3f,.025f),material);
                        crack.transform.localRotation=Quaternion.LookRotation(normal)*Quaternion.Euler(0,0,k*37);
                    }
                }
                Particles(visual.fallingBody.transform,"Falling trail",spell.tint,.6f,35,.3f);
                string source=spell.kind==AdvancedSpellKind.Meteor?"AoE effects/Ground AOE explosion.prefab":spell.kind==AdvancedSpellKind.ShardVortex?"AoE effects/Red energy explosion.prefab":"AoE effects/Crystals crossfade.prefab";
                visual.impactPrefab=Imported(source,id+"Impact",false);
                if(spell.kind==AdvancedSpellKind.Meteor)visual.extraImpactPrefab=Imported("Hits and explosions/Explosion.prefab","MeteorExplosion",false);
            }
            visual.area=new GameObject("Active area"); visual.area.transform.SetParent(root.transform,false);
            if(spell.kind==AdvancedSpellKind.SteamLens)
            {
                root.AddComponent<SteamLens>();
                var ringRoot=new GameObject("Fire ring");ringRoot.transform.SetParent(visual.area.transform,false);
                Ring(ringRoot.transform,1.1f,Solid("LensFire",new Color(1,.35f,.06f)));
                ringRoot.transform.localRotation=Quaternion.Euler(90,0,0);
                for(int i=0;i<12;i++)
                {
                    float angle=i*Mathf.PI/6;
                    var shard=Mesh(visual.area.transform,"Ice lens shard",PrimitiveType.Cube,new Vector3(Mathf.Cos(angle)*.9f,Mathf.Sin(angle)*.9f,0),new Vector3(.13f,.35f,.07f),material);
                    shard.GetComponent<MeshFilter>().sharedMesh=CrystalMesh();
                    shard.transform.localRotation=Quaternion.Euler(0,0,i*30-90);
                }
                var air=Particles(ringRoot.transform,"Air and embers",new Color(.7f,.9f,1),1,25,.05f);
                var velocity=air.velocityOverLifetime;velocity.enabled=true;velocity.orbitalY=2;
                visual.impactPrefab=Imported("Hits and explosions/Snow hit.prefab","LensShatter",false);
            }
            else if(spell.kind==AdvancedSpellKind.IceBridge)
            {
                var platform=Mesh(visual.area.transform,"Ice platform",PrimitiveType.Cube,Vector3.zero,spell.bridgeSize,material);
                // коллайдер живёт на сетевом корне и остаётся активным на выделенном сервере.
                root.AddComponent<BoxCollider>().size=spell.bridgeSize;
                root.AddComponent<IceBridgeSurface>();
                for(int i=0;i<9;i++)Mesh(visual.area.transform,"Frost seam",PrimitiveType.Cube,new Vector3(0,.11f,i-4),new Vector3(2.9f,.015f,.025f),Solid("Frost",Color.white));
            }
            else if(spell.kind==AdvancedSpellKind.ScaldingMist || spell.kind==AdvancedSpellKind.BoilingIce || spell.kind==AdvancedSpellKind.ThermalSpring)
            {
                var smoke=Object.Instantiate(Imported("Smoke effects/Smoke hemisphere loop.prefab",id+"Steam",true,spell.tint),visual.area.transform);
                smoke.name="Steam"; smoke.transform.localPosition=Vector3.zero; smoke.transform.localScale=Vector3.one*(spell.kind==AdvancedSpellKind.ScaldingMist?.55f:.35f);
                if(spell.kind==AdvancedSpellKind.ThermalSpring)
                    foreach(var ps in smoke.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                        var emission=ps.emission;emission.rateOverTimeMultiplier*=.2f;
                        var main=ps.main;main.startSizeMultiplier*=.6f;main.startColor=new Color(.8f,1,.93f,.3f);
                    }
                Ring(visual.area.transform,spell.radius,material);
                if(spell.kind==AdvancedSpellKind.ThermalSpring)
                {
                    var marks=new List<Transform>();
                    for(int i=0;i<6;i++)
                    {
                        var mark=new GameObject("Healing +"); mark.transform.SetParent(visual.area.transform,false);
                        Mesh(mark.transform,"Vertical",PrimitiveType.Cube,Vector3.zero,new Vector3(.06f,.25f,.035f),material);
                        Mesh(mark.transform,"Horizontal",PrimitiveType.Cube,Vector3.zero,new Vector3(.25f,.06f,.035f),material);
                        marks.Add(mark.transform);
                    }
                    visual.healthMarks=marks.ToArray();
                }
                if(spell.kind==AdvancedSpellKind.BoilingIce)
                    for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Mesh(visual.area.transform,"Ice fragment",PrimitiveType.Cube,new Vector3(Mathf.Cos(a),.12f,Mathf.Sin(a)),new Vector3(.2f,.25f,.3f),material);}
            }
            else if(spell.kind==AdvancedSpellKind.Meteor)
            {
                Ring(visual.area.transform,spell.radius,material);
                Particles(visual.area.transform,"Burning ground",spell.tint,spell.radius,45,.35f);
            }
            if(visual.warning!=null)visual.warning.SetActive(false);
            if(visual.projectile!=null)visual.projectile.SetActive(false);
            if(visual.fallingBody!=null)visual.fallingBody.SetActive(false);
            visual.area.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { Object.DestroyImmediate(root); }
        RestoreIdentity(path);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // исходные эффекты пакета не редактируем; сохраняем подготовленные копии для urp без света и коллизий.
    static GameObject Imported(string source,string id,bool loop,Color? tint=null)
    {
        string path=Folder+"/"+id+"Visual.prefab";
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(existing!=null)return existing;
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Pack+source);
        if(asset==null)throw new InvalidOperationException("Missing effect: "+source);
        var root=Object.Instantiate(asset); root.name=id;
        foreach(var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(behaviour);
        foreach(var collider in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
        foreach(var light in root.GetComponentsInChildren<Light>(true))light.enabled=false;
        foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=loop;main.playOnAwake=true;main.stopAction=ParticleSystemStopAction.None;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            main.maxParticles=Mathf.Min(main.maxParticles,250);
            if(tint.HasValue)
            {
                main.startColor=tint.Value;
                var colors=ps.colorOverLifetime;
                if(colors.enabled){var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.6f,.2f),new GradientAlphaKey(0,1)});colors.color=gradient;}
            }
            var lights=ps.lights;lights.enabled=false;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m!=null?IceAndShieldEffectsBuilder.ConvertMaterial(m):null).ToArray();
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        var result=PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);return result;
    }

    static Material Solid(string id,Color tint)
    {
        string path=Folder+"/"+id+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.color=tint;AssetDatabase.CreateAsset(mat,path);}return mat;
    }
    // шесть граней и два острых конца дают читаемый кристалл вместо прямоугольного блока.
    static UnityEngine.Mesh CrystalMesh()
    {
        const string path=Folder+"/FallingCrystal.asset";
        var existing=AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path);if(existing!=null)return existing;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<6;i++)
        {
            float a=i*Mathf.PI/3,b=(i+1)*Mathf.PI/3;
            Vector3 left=new Vector3(Mathf.Cos(a)*.5f,.2f,Mathf.Sin(a)*.5f),right=new Vector3(Mathf.Cos(b)*.5f,.2f,Mathf.Sin(b)*.5f);
            int start=vertices.Count;vertices.AddRange(new[]{Vector3.up,left,right,Vector3.down,left,right});
            triangles.AddRange(new[]{start,start+2,start+1,start+3,start+4,start+5});
        }
        var mesh=new UnityEngine.Mesh{name="Falling crystal"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static GameObject Mesh(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;return go;
    }
    static void Ring(Transform parent,float radius,Material material)
    {
        var go=new GameObject("Area boundary");go.transform.SetParent(parent,false);var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.widthMultiplier=.055f;line.sharedMaterial=material;line.positionCount=64;
        for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;line.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,.035f,Mathf.Sin(a)*radius));}
    }
    static ParticleSystem Particles(Transform parent,string name,Color color,float radius,float rate,float size)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localRotation=Quaternion.Euler(-90,0,0);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=true;main.startLifetime=.8f;main.startSpeed=.8f;main.startSize=size;main.startColor=color;main.maxParticles=100;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
        var emission=ps.emission;emission.rateOverTime=rate;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=radius;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Other Asstets/GeneratedWizard/ReadableSpellParticles.mat");return ps;
    }

    static void RecolorSeal()
    {
        const string path="Assets/Scripts/Spells/Tactical/FireSeal.asset";Backup(path);
        var spell=AssetDatabase.LoadAssetAtPath<TacticalSpell>(path);var data=new SerializedObject(spell);
        data.FindProperty("displayName").stringValue="Термическая печать";var recipe=data.FindProperty("recipe");
        for(int i=0;i<3;i++)recipe.GetArrayElementAtIndex(i).enumValueIndex=new[]{0,2,3}[i];
        data.ApplyModifiedPropertiesWithoutUndo();spell.tint=new Color(.65f,.62f,.6f);EditorUtility.SetDirty(spell);
        string prefabPath=AssetDatabase.GetAssetPath(spell.effectPrefab);Backup(prefabPath);
        var root=PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
                {
                    if(source==null)return null;
                    string materialPath=Folder+"/Seal_"+source.name+".mat";
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if(mat==null){mat=new Material(source);if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",new Color(.65f,.62f,.6f,1));if(mat.HasProperty("_TintColor"))mat.SetColor("_TintColor",new Color(.65f,.62f,.6f,1));AssetDatabase.CreateAsset(mat,materialPath);}return mat;
                }).ToArray();
            }
            foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main=ps.main;main.startColor=new Color(.7f,.67f,.65f);
                var colors=ps.colorOverLifetime;
                if(colors.enabled)
                {
                    var gradient=new Gradient();
                    gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                        new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(1,.8f),new GradientAlphaKey(0,1)});
                    colors.color=gradient;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}RestoreIdentity(prefabPath);
    }

    static void RestoreIdentity(string path)
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);var identity=root.GetComponent<NetworkIdentity>();
        if(identity==null)return;uint id=identity.assetId;EditorUtility.SetDirty(identity);PrefabUtility.SavePrefabAsset(root);
    }
    static void Backup(string path)
    {
        if(!File.Exists(path))return;string copy="Logs/AdvancedSpellsBefore/"+path;
        if(File.Exists(copy))return;Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(path,copy);
    }
    static void AddReference(SerializedProperty list,Object value)
    {
        for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==value)return;
        list.GetArrayElementAtIndex(list.arraySize++).objectReferenceValue=value;
    }

    static void UpdateRoot(GameObject root,List<AdvancedSpell> spells)
    {
        foreach(var manager in root.GetComponentsInChildren<NetworkManager>(true))
        {foreach(var spell in spells)if(!manager.spawnPrefabs.Contains(spell.effectPrefab))manager.spawnPrefabs.Add(spell.effectPrefab);EditorUtility.SetDirty(manager);}
        foreach(var ui in root.GetComponentsInChildren<ShelfSpellCatalogUI>(true))AppendCards(ui,"cards",spells,0);
        foreach(var ui in root.GetComponentsInChildren<ElementLoadoutUI>(true))AppendCards(ui,"spellCards",spells,1);
        foreach(var ui in root.GetComponentsInChildren<PlayerGameUI>(true))AppendCards(ui,"cards",spells,2);
    }

    // копируем существующий дизайн карточек и переназначаем ссылки, не меняя размеры и расположение интерфейса.
    static void AppendCards(Component ui,string field,List<AdvancedSpell> spells,int kind)
    {
        var data=new SerializedObject(ui);var list=data.FindProperty(field);if(list==null||list.arraySize==0)return;
        foreach(var spell in spells)
        {
            bool exists=false;for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).FindPropertyRelative("spell").objectReferenceValue==spell)exists=true;
            if(exists)continue;
            var template=list.GetArrayElementAtIndex(0);var oldSpell=(Spell)template.FindPropertyRelative("spell").objectReferenceValue;
            var source=kind==1?((Image)template.FindPropertyRelative("background").objectReferenceValue).gameObject:(GameObject)template.FindPropertyRelative("root").objectReferenceValue;
            var clone=Object.Instantiate(source,source.transform.parent);clone.name=spell.name;
            foreach(var icon in clone.GetComponentsInChildren<SpellIconGraphic>(true)){icon.spell=spell;icon.color=spell.tint;}
            foreach(var text in clone.GetComponentsInChildren<Text>(true))
            {
                if(text.text==oldSpell.Name)text.text=spell.Name;
                else if(text.text==oldSpell.Description)text.text=spell.Description;
                else if(text.text==string.Join(" + ",oldSpell.Recipe.Select(ElementLoadout.Label))||text.text==string.Join(" → ",oldSpell.Recipe.Select(ElementLoadout.Label)))text.text=string.Join(" → ",spell.Recipe.Select(ElementLoadout.Label));
            }
            int index=list.arraySize++;template=list.GetArrayElementAtIndex(0);var card=list.GetArrayElementAtIndex(index);card.FindPropertyRelative("spell").objectReferenceValue=spell;
            if(kind!=1)card.FindPropertyRelative("root").objectReferenceValue=clone;
            string[] fields=kind==0?new[]{"recipe"}:kind==1?new[]{"background","status"}:new[]{"keys","icon","cover","seconds"};
            foreach(string name in fields)
            {
                var original=(Component)template.FindPropertyRelative(name).objectReferenceValue;
                card.FindPropertyRelative(name).objectReferenceValue=Map(original,source.transform,clone.transform);
            }
            clone.SetActive(spell.IsAvailable(ElementLoadout.Default));
        }
        // старые подписи печати хранятся в сцене; обновляем их вместе с рецептом.
        for(int i=0;i<list.arraySize;i++)
        {
            var card=list.GetArrayElementAtIndex(i);var spell=card.FindPropertyRelative("spell").objectReferenceValue as Spell;
            if(spell==null||spell.name!="FireSeal")continue;
            var root=kind==1?((Image)card.FindPropertyRelative("background").objectReferenceValue).gameObject:(GameObject)card.FindPropertyRelative("root").objectReferenceValue;
            foreach(var text in root.GetComponentsInChildren<Text>(true))
            {
                if(text.text=="Огненная печать")text.text=spell.Name;
                if(text.text.Contains("Огонь + Огонь + Земля")||text.text.Contains("Огонь → Огонь → Земля"))text.text="Огонь → Лёд → Земля";
            }
            foreach(var icon in root.GetComponentsInChildren<SpellIconGraphic>(true))icon.color=SpellIconGraphic.Tint(spell);
        }
        data.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(ui);
    }
    static Component Map(Component source,Transform root,Transform destination)
    {
        if(source==null)return null;var path=new Stack<int>();var node=source.transform;
        while(node!=root){path.Push(node.GetSiblingIndex());node=node.parent;}
        while(path.Count>0)destination=destination.GetChild(path.Pop());return destination.GetComponent(source.GetType());
    }

    [MenuItem("Tools/Wizard War/Validate advanced spells")]
    public static void Validate()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<SpellManager>(true).Spells;
        if(catalog.Count!=29||catalog.Select(s=>string.Join(",",s.Recipe)).Distinct().Count()!=29)throw new Exception("Catalog count or duplicate recipe.");
        var report=new List<string>{"PASS: 29 unique recipes; eight spells in every loadout."};
        for(int a=0;a<5;a++)for(int b=a+1;b<5;b++)for(int c=b+1;c<5;c++)
        {
            var loadout=new ElementLoadout{q=(MagicElement)a,e=(MagicElement)b,r=(MagicElement)c};int count=catalog.Count(s=>s.IsAvailable(loadout));
            if(count!=8)throw new Exception("Wrong loadout count: "+a+b+c+" = "+count);
            report.Add($"{loadout.q}/{loadout.e}/{loadout.r}: {count}");
        }
        foreach(var spell in catalog.OfType<AdvancedSpell>())
        {
            var prefab=spell.effectPrefab;if(prefab==null||prefab.GetComponent<NetworkIdentity>().assetId==0)throw new Exception("Missing network prefab: "+spell.name);
            if(prefab.GetComponent<AdvancedSpellEffect>().definition!=spell)throw new Exception("Wrong definition: "+spell.name);
            report.Add($"{spell.name}: delay={spell.delay}, impact={spell.impactDamage}, tick={spell.tickDamage}, duration={spell.duration}, cooldown={spell.Cooldown}");
        }
        Scene original=SceneManager.GetActiveScene();
        int managers=0,interfaces=0;
        foreach(string path in new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"})
        {
            Scene scene=SceneManager.GetSceneByPath(path);bool loaded=scene.IsValid()&&scene.isLoaded;
            if(!loaded)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                foreach(var root in scene.GetRootGameObjects())
                {
                    foreach(var manager in root.GetComponentsInChildren<NetworkManager>(true))
                    {
                        managers++;
                        foreach(var spell in catalog.OfType<AdvancedSpell>())
                            if(!manager.spawnPrefabs.Contains(spell.effectPrefab))throw new Exception("Unregistered network prefab: "+spell.name);
                    }
                    foreach(var ui in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        string field=ui is ElementLoadoutUI?"spellCards":ui is ShelfSpellCatalogUI||ui is PlayerGameUI?"cards":null;
                        if(field==null)continue;
                        var cards=new SerializedObject(ui).FindProperty(field);
                        if(cards.arraySize!=29)throw new Exception("Wrong UI card count: "+ui.name+" = "+cards.arraySize);
                        var entries=new HashSet<Spell>();
                        for(int i=0;i<cards.arraySize;i++)entries.Add(cards.GetArrayElementAtIndex(i).FindPropertyRelative("spell").objectReferenceValue as Spell);
                        if(entries.Count!=29||entries.Contains(null)||catalog.Any(spell=>!entries.Contains(spell)))throw new Exception("Incomplete UI catalog: "+ui.name);
                        interfaces++;
                    }
                }
            }
            finally{if(!loaded)EditorSceneManager.CloseScene(scene,true);}
        }
        if(original.IsValid())SceneManager.SetActiveScene(original);
        if(managers==0||interfaces==0)throw new Exception("Missing managers or UI.");
        report.Add($"PASS: {managers} network managers and {interfaces} interfaces contain all new spells.");
        Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/advanced-spells-validation.txt",report);
    }
}
