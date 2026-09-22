using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// создаёт шесть тактических заклинаний, их префабы и карточки в сохранённых интерфейсах.
public static class TacticalSpellBuilder
{
    const string Folder="Assets/TacticalSpells";
    public static readonly string[] Ids={"SteamDash","IceMirror","StoneWall","FireSeal","GravityWell","SnowDecoy"};
    static readonly string[] Titles={"Паровой рывок","Ледяное зеркало","Каменная стена","Огненная печать","Гравитационный узел","Снежный двойник"};
    static readonly string[] Texts={"Рывок по направлению движения на 5 м. Оставляет облако пара. Стены останавливают рывок.","Щит перед магом на 3 с. Отражает один вражеский снаряд обратно и разрушается.","Стена высотой 2,7 м на 5 с. Перекрывает проход и снаряды. Только на свободной земле.","Ловушка на 9 с; взводится за 1 с. Враг в радиусе 1,25 м вызывает взрыв: 35 урона в радиусе 3 м.","Узел на 3,5 с притягивает врагов в радиусе 4 м. Не действует через стены и не наносит урон.","Копия мага бежит вперёд 4 с. При попадании замедляет ближайшего врага в радиусе 3 м на 50% на 2 с."};
    static readonly int[][] Recipes={new[]{1,1,1},new[]{2,2,3},new[]{1,3,3},new[]{0,0,3},new[]{3,4,4},new[]{1,2,4}};
    static readonly float[] Durations={2.5f,3,5,9,3.5f,4};
    static readonly float[] Cooldowns={7,12,10,12,12,10};
    static readonly float[] Radii={1,1.1f,0,3,4,3};
    static readonly Color[] Colors={new Color(.8f,.92f,1),new Color(.25f,.8f,1),new Color(.55f,.4f,.22f),new Color(1,.3f,.03f),new Color(.6f,.25f,1),new Color(.55f,.9f,1)};
    static readonly List<string> changed=new List<string>();
    // записываем стандартные параметры, расширяем каталог игрока и регистрируем сетевые префабы в сценах.
    [MenuItem("Wizard/Spells/Install six tactical spells")]
    public static void Apply()
    {
        changed.Clear();
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets","TacticalSpells");
        var catalog=new List<TacticalSpell>();
        for(int i=0;i<6;i++)
        {
            string path="Assets/Scripts/Spells/Tactical/"+Ids[i]+".asset";
            var spell=AssetDatabase.LoadAssetAtPath<TacticalSpell>(path);
            if(spell==null){spell=ScriptableObject.CreateInstance<TacticalSpell>();AssetDatabase.CreateAsset(spell,path);}
            var data=new SerializedObject(spell);data.FindProperty("displayName").stringValue=Titles[i];data.FindProperty("description").stringValue=Texts[i];data.FindProperty("cooldown").floatValue=Cooldowns[i];
            var recipe=data.FindProperty("recipe");recipe.arraySize=3;for(int j=0;j<3;j++)recipe.GetArrayElementAtIndex(j).enumValueIndex=Recipes[i][j];data.ApplyModifiedPropertiesWithoutUndo();
            spell.kind=(TacticalKind)i;spell.tint=Colors[i];spell.duration=Durations[i];spell.radius=Radii[i];spell.damage=i==3?35:0;
            spell.effectPrefab=BuildEffect(spell,Ids[i]);EditorUtility.SetDirty(spell);catalog.Add(spell);changed.Add(path);
        }
        string playerPath="Assets/Prefabs/Player.prefab";var player=PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            var manager=player.GetComponentInChildren<SpellManager>(true);var data=new SerializedObject(manager);var spells=data.FindProperty("spells");
            foreach(var spell in catalog)AddReference(spells,spell);data.ApplyModifiedPropertiesWithoutUndo();
            var keys=new HashSet<string>();foreach(var spell in manager.Spells)
            {string key=string.Join(",",spell.Recipe);if(!keys.Add(key))throw new Exception("Duplicate spell recipe: "+spell.Name);}
            PrefabUtility.SaveAsPrefabAsset(player,playerPath);changed.Add(playerPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(player);}
        foreach(string path in new[]{"Assets/Prefabs/UI/SpellbookUI.prefab","Assets/Prefabs/UI/MatchUI.prefab"})
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try{AppendCards(root,catalog);PrefabUtility.SaveAsPrefabAsset(root,path);changed.Add(path);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        foreach(string path in new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"})
        {
            var scene=EditorSceneManager.OpenScene(path);
            foreach(var root in scene.GetRootGameObjects())AppendCards(root,catalog);
            foreach(var manager in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {foreach(var spell in catalog)if(!manager.spawnPrefabs.Contains(spell.effectPrefab))manager.spawnPrefabs.Add(spell.effectPrefab);EditorUtility.SetDirty(manager);}
            EditorSceneManager.SaveScene(scene);changed.Add(path);
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/tactical-files.txt",changed.Distinct());
        Debug.Log("TACTICAL_SPELLS_INSTALLED: six unique recipes, saved UI cards and network prefabs");
    }
    // добавляем ссылку в сериализованный список только при её отсутствии.
    static void AddReference(SerializedProperty list,Object value)
    {for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==value)return;int index=list.arraySize++;list.GetArrayElementAtIndex(index).objectReferenceValue=value;}
    // создаём или обновляем одноцветный материал тактического эффекта.
    static Material Material(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",color);EditorUtility.SetDirty(material);changed.Add(path);return material;
    }
    // строим геометрию и коллайдеры выбранной механики, затем сохраняем префаб с устойчивым сетевым идентификатором.
    static GameObject BuildEffect(TacticalSpell spell,string id)
    {
        var root=new GameObject(id);root.AddComponent<NetworkIdentity>();root.AddComponent<NetworkTransformReliable>().syncDirection=SyncDirection.ServerToClient;
        var body=root.AddComponent<Rigidbody>();body.useGravity=false;body.isKinematic=true;
        root.AddComponent<TacticalEffect>().definition=spell;var visual=root.AddComponent<TacticalVisual>();
        var material=Material(id,spell.tint);var accent=Material(id+"Accent",Color.Lerp(spell.tint,Color.white,.5f));
        if(spell.kind==TacticalKind.StoneWall)
        {
            var box=root.AddComponent<BoxCollider>();box.size=new Vector3(3.8f,2.7f,.55f);box.center=new Vector3(0,1.35f,0);
            for(int i=0;i<6;i++)Mesh(root,"Stone",PrimitiveType.Cube,new Vector3((i%3-1)*1.25f,.66f+(i/3)*1.34f,0),new Vector3(1.22f,1.28f,.55f),i%2==0?material:accent);
        }
        else if(spell.kind==TacticalKind.IceMirror)
        {
            var box=root.AddComponent<BoxCollider>();box.isTrigger=true;box.size=new Vector3(2.2f,2.5f,.2f);box.center=Vector3.up*.25f;
            for(int i=0;i<2;i++)Mesh(root,"Mirror edge",PrimitiveType.Cube,new Vector3(i==0?-1.05f:1.05f,.25f,0),new Vector3(.1f,2.5f,.1f),accent);
            for(int i=0;i<2;i++)Mesh(root,"Mirror edge",PrimitiveType.Cube,new Vector3(0,i==0?-1:1.5f,0),new Vector3(2.2f,.1f,.1f),accent);
            // оставляем у зеркала открытую искрящуюся рамку, чтобы она не закрывала обзор игроку.
            Particles(root,spell,1,material);
        }
        else if(spell.kind==TacticalKind.SnowDecoy)
        {
            var capsule=root.AddComponent<CapsuleCollider>();capsule.isTrigger=true;capsule.radius=.45f;capsule.height=2.5f;capsule.center=Vector3.up*1.25f;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<WizardAppearance>(true).visualRoot;
            var model=Object.Instantiate(source.gameObject,root.transform);model.name="Wizard double";model.transform.localPosition=source.localPosition+Vector3.up;model.transform.localRotation=source.localRotation;
            foreach(var collider in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
        }
        else
        {
            if(spell.kind!=TacticalKind.SteamDash)
            {
                visual.ring=Ring(root,spell.kind==TacticalKind.FireSeal?1.25f:spell.radius,material);
                if(spell.kind==TacticalKind.FireSeal)
                    for(int i=0;i<3;i++){float angle=i*120*Mathf.Deg2Rad;var mark=Mesh(root,"Rune",PrimitiveType.Cube,new Vector3(Mathf.Cos(angle)*.5f,.03f,Mathf.Sin(angle)*.5f),new Vector3(.9f,.035f,.07f),accent);mark.localRotation=Quaternion.Euler(0,-i*120,0);}
                if(spell.kind==TacticalKind.GravityWell)visual.core=Mesh(root,"Gravity core",PrimitiveType.Sphere,Vector3.up*.65f,Vector3.one*.35f,material);
            }
            Particles(root,spell,spell.kind==TacticalKind.GravityWell?spell.radius:1,material);
        }
        string path=Folder+"/"+id+".prefab";
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);
        var identity=new SerializedObject(prefab.GetComponent<NetworkIdentity>());identity.FindProperty("_assetId").longValue=NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)));identity.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SavePrefabAsset(prefab);changed.Add(path);return prefab;
    }
    // создаём декоративный примитив без собственного коллайдера.
    static Transform Mesh(GameObject root,string name,PrimitiveType type,Vector3 pos,Vector3 scale,Material material)
    {var go=GameObject.CreatePrimitive(type);go.name=name;Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(root.transform,false);go.transform.localPosition=pos;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;}
    // рисуем замкнутую границу радиуса в локальных координатах эффекта.
    static LineRenderer Ring(GameObject root,float radius,Material material)
    {var go=new GameObject("Radius",typeof(LineRenderer));go.transform.SetParent(root.transform,false);var line=go.GetComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.widthMultiplier=.07f;line.positionCount=64;line.sharedMaterial=material;for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;line.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,.035f,Mathf.Sin(a)*radius));}return line;}
    // добавляем частицы для выбранной механики, включая движение к центру гравитационного узла.
    static void Particles(GameObject root,TacticalSpell spell,float radius,Material unused)
    {
        var go=new GameObject("Particles",typeof(ParticleSystem));go.transform.SetParent(root.transform,false);go.transform.localRotation=Quaternion.Euler(-90,0,0);
        var ps=go.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.loop=true;main.playOnAwake=true;main.startLifetime=1.2f;main.maxParticles=100;main.startSpeed=.4f;main.startSize=spell.kind==TacticalKind.SteamDash?.45f:.12f;main.startColor=spell.tint;
        var emission=ps.emission;emission.rateOverTime=35;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=radius;
        if(spell.kind==TacticalKind.GravityWell){var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.radial=-2;main.startSpeed=0;}
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Other Asstets/GeneratedWizard/ReadableSpellParticles.mat");
    }
    // дополняем обе разновидности интерфейса: книгу заклинаний и панель матча.
    static void AppendCards(GameObject root,List<TacticalSpell> spells)
    {
        foreach(var book in root.GetComponentsInChildren<ElementLoadoutUI>(true))Append(book,"spellCards",true,spells);
        foreach(var hud in root.GetComponentsInChildren<PlayerGameUI>(true))Append(hud,"cards",false,spells);
    }
    // находим соответствующий компонент в копии карточки по цепочке индексов дочерних объектов.
    static Component Map(Component original,Transform source,Transform destination)
    {if(original==null)return null;var indices=new Stack<int>();var t=original.transform;while(t!=source){indices.Push(t.GetSiblingIndex());t=t.parent;}while(indices.Count>0)destination=destination.GetChild(indices.Pop());return destination.GetComponent(original.GetType());}
    // клонируем шаблон карточки для отсутствующих заклинаний и переназначаем её внутренние ссылки.
    static void Append(Component ui,string field,bool book,List<TacticalSpell> spells)
    {
        var data=new SerializedObject(ui);var list=data.FindProperty(field);if(list.arraySize==0)throw new Exception("No card template on "+ui.name);
        foreach(var spell in spells)
        {
            bool exists=false;for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).FindPropertyRelative("spell").objectReferenceValue==spell)exists=true;if(exists)continue;
            var template=list.GetArrayElementAtIndex(0);var sourceSpell=(Spell)template.FindPropertyRelative("spell").objectReferenceValue;
            GameObject source=book?((Image)template.FindPropertyRelative("background").objectReferenceValue).gameObject:(GameObject)template.FindPropertyRelative("root").objectReferenceValue;
            var clone=Object.Instantiate(source,source.transform.parent);clone.name=spell.name;
            // после клонирования заменяем ссылки карточки на её собственные дочерние объекты.
            foreach(var icon in clone.GetComponentsInChildren<SpellIconGraphic>(true)){icon.spell=spell;icon.color=SpellIconGraphic.Tint(spell);}
            foreach(var text in clone.GetComponentsInChildren<Text>(true))
            {if(text.text==sourceSpell.Name)text.text=spell.Name;else if(text.text==sourceSpell.Description)text.text=spell.Description;else if(text.text==string.Join(" + ",sourceSpell.Recipe.Select(ElementLoadout.Label)))text.text=string.Join(" + ",spell.Recipe.Select(ElementLoadout.Label));}
            int index=list.arraySize++;template=list.GetArrayElementAtIndex(0);var card=list.GetArrayElementAtIndex(index);card.FindPropertyRelative("spell").objectReferenceValue=spell;
            if(book)
            {
                card.FindPropertyRelative("background").objectReferenceValue=clone.GetComponent<Image>();var text=(Text)Map((Text)template.FindPropertyRelative("status").objectReferenceValue,source.transform,clone.transform);text.text=ElementLoadout.Default.KeysFor(spell.Recipe)+"\n"+spell.Cooldown+" с";card.FindPropertyRelative("status").objectReferenceValue=text;
            }
            else
            {
                card.FindPropertyRelative("root").objectReferenceValue=clone;
                foreach(string name in new[]{"keys","icon","cover","seconds"})card.FindPropertyRelative(name).objectReferenceValue=Map((Component)template.FindPropertyRelative(name).objectReferenceValue,source.transform,clone.transform);
                ((Text)card.FindPropertyRelative("keys").objectReferenceValue).text=ElementLoadout.Default.KeysFor(spell.Recipe).Replace(" → ","");
            }
            clone.SetActive(spell.IsAvailable(ElementLoadout.Default));
        }
        data.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(ui);
    }
}

