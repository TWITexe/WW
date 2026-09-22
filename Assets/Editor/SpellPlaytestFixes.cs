using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// применяем конкретные поправки плейтеста один раз, сохраняя размеры остальных эффектов и текущую сцену.
public static class SpellPlaytestFixes
{
    const string Marker = "Logs/spell-playtest-fixes-applied.txt";
    static readonly Dictionary<string,string> texts = new Dictionary<string,string>();

    // фоновый запуск использует те же редакторские api и завершается после применения и проверок.
    public static void ApplyAndValidate()
    {
        Apply();
        // фоновый редактор начинает с безымянной сцены; тестам нужна сохранённая исходная сцена.
        if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);
        AdvancedSpellRegression.Run();
        File.WriteAllText("Logs/spell-playtest-fixes-validation.txt", "PASS: playtest asset changes and advanced regression, including nested player owner and delayed ice explosion.\n" + DateTime.Now.ToString("O"));
    }

    [MenuItem("Tools/Wizard War/Apply spell playtest fixes")]
    public static void Apply()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play Mode before applying asset changes.");
        if(File.Exists(Marker))return;
        texts.Clear();
        Resize("ScaldingMist",5.75f,true);
        Resize("BoilingIce",6.25f,false);
        var smoke=AssetDatabase.LoadAssetAtPath<ElementalSpell>("Assets/Scripts/Spells/Elemental/SmokeCloud.asset");
        Backup(AssetDatabase.GetAssetPath(smoke));
        var data=new SerializedObject(smoke);
        texts[smoke.Name]="Дымовая завеса";
        data.FindProperty("displayName").stringValue="Дымовая завеса";
        data.ApplyModifiedPropertiesWithoutUndo();
        EditPrefab("Assets/Prefabs/Player.prefab",root=>
        {
            var movement=root.GetComponentInChildren<RelativeMovement>(true);
            var settings=new SerializedObject(movement);
            settings.FindProperty("moveSpeed").floatValue=9;
            settings.FindProperty("sprintMultiplier").floatValue=1.5f;
            settings.ApplyModifiedPropertiesWithoutUndo();
        });
        foreach(string path in new[]{"Assets/Prefabs/UI/SpellbookUI.prefab","Assets/Prefabs/UI/MatchUI.prefab"})
            if(File.Exists(path))EditPrefab(path,UpdateText);
        var original=SceneManager.GetActiveScene();
        foreach(string path in new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"})
        {
            Backup(path);
            var scene=SceneManager.GetSceneByPath(path);bool loaded=scene.IsValid()&&scene.isLoaded;
            if(!loaded)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try{foreach(var root in scene.GetRootGameObjects())UpdateText(root);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
            finally{if(!loaded)EditorSceneManager.CloseScene(scene,true);}
        }
        if(original.IsValid())SceneManager.SetActiveScene(original);
        AssetDatabase.SaveAssets();
        File.WriteAllText(Marker,"Applied: mist radius 5.75, emission x4; ice radius 6.25, delay .8; walk 9, sprint 13.5; smoke screen title.\n"+DateTime.Now.ToString("O"));
    }

    static void Resize(string id,float radius,bool denser)
    {
        var spell=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/"+id+".asset");
        Backup(AssetDatabase.GetAssetPath(spell));
        string oldDescription=spell.Description;
        spell.radius=radius;
        if(!denser)spell.impactDelay=.8f;
        string description=denser
            ? "Оранжевый туман радиусом 5,75 м на 3 с: 5 урона каждые 0,5 с. Снаряд создаёт плотную завесу при столкновении."
            : "После столкновения ледяное ядро взрывается через 0,8 с. Область радиусом 6,25 м наносит 7 урона в секунду в течение 5 с.";
        texts[oldDescription]=description;
        var data=new SerializedObject(spell);data.FindProperty("description").stringValue=description;data.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(spell);
        EditPrefab(AssetDatabase.GetAssetPath(spell.effectPrefab),root=>
        {
            var visual=root.GetComponent<AdvancedSpellVisual>();
            var scale=visual.area.transform.localScale;scale.x*=2.5f;scale.z*=2.5f;visual.area.transform.localScale=scale;
            if(denser)
            {
                foreach(var particles in visual.area.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=particles.main;main.maxParticles*=4;
                    var emission=particles.emission;emission.rateOverTimeMultiplier*=4;emission.rateOverDistanceMultiplier*=4;
                    for(int i=0;i<emission.burstCount;i++){var burst=emission.GetBurst(i);burst.count=new ParticleSystem.MinMaxCurve(burst.count.constantMin*4,burst.count.constantMax*4);emission.SetBurst(i,burst);}
                }
            }
            else
            {
                var boundary=visual.area.GetComponentInChildren<LineRenderer>(true);
                if(boundary!=null)
                {
                    visual.warning=Object.Instantiate(boundary.gameObject,root.transform);
                    visual.warning.name="Delayed explosion boundary";visual.warning.transform.localScale=scale;visual.warning.SetActive(false);
                }
                visual.impactPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spells/Advanced/LensShatterVisual.prefab");
            }
        });
    }

    static void EditPrefab(string path,Action<GameObject> edit)
    {
        Backup(path);var root=PrefabUtility.LoadPrefabContents(path);
        try{edit(root);PrefabUtility.SaveAsPrefabAsset(root,path);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);var identity=asset.GetComponent<NetworkIdentity>();
        if(identity!=null){uint id=identity.assetId;EditorUtility.SetDirty(identity);PrefabUtility.SavePrefabAsset(asset);}
    }

    static void UpdateText(GameObject root)
    {
        foreach(var label in root.GetComponentsInChildren<Text>(true))
            if(texts.TryGetValue(label.text,out string replacement))label.text=replacement;
    }

    static void Backup(string path)
    {
        string copy="Logs/SpellPlaytestFixesBefore/"+path;
        if(File.Exists(copy))return;Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(path,copy);
    }
}
