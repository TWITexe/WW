using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Mirror;

// проверяет исходное расширение на четырнадцать заклинаний; это ожидание не учитывает поздние тактические дополнения.
public static class WizardExpansionValidation
{
    // проверяем рецепты, допустимые наборы, сетевые идентификаторы и части модели исходного расширения.
    [MenuItem("Tools/Wizard War/Validate wizard expansion")]
    public static void Run()
    {
        int checks=0;
        Action<bool,string> check=(value,message)=> { if(!value) throw new Exception(message); checks++; };
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var manager=player.GetComponentInChildren<SpellManager>(true);
        check(manager.Spells.Count==14,"Expected 2 original + 12 new spells");
        var recipes=new HashSet<string>();
        var networkIds=new HashSet<long>();
        foreach(var spell in manager.Spells)
        {
            check(spell!=null && spell.Recipe.Count==3,"Valid recipe");
            check(recipes.Add(string.Join(",",spell.Recipe)),"Duplicate recipe: "+spell.Name);
            check(!string.IsNullOrWhiteSpace(spell.Description),"Description: "+spell.Name);
            var recipe=spell.Recipe;
            check(spell.MatchesCombo(recipe),"Exact sequence: "+spell.Name);
            check(spell.MatchesCombo(new[]{recipe[2],recipe[0],recipe[1]}) ==
                (recipe[0]==recipe[1] && recipe[1]==recipe[2]),"Ordered permutation: "+spell.Name);
            if(spell is ElementalSpell elemental && elemental.mode!=ElementalCastMode.Shield)
            {
                check(elemental.effectPrefab!=null,"Effect prefab");
                check(elemental.effectPrefab.GetComponent<ElementalEffect>().definition==elemental,"Definition link");
                check(elemental.effectPrefab.GetComponent<NetworkTransformReliable>().syncDirection==SyncDirection.ServerToClient,"Server sync");
                long id=new SerializedObject(elemental.effectPrefab.GetComponent<NetworkIdentity>()).FindProperty("_assetId").longValue;
                check(id!=0 && networkIds.Add(id),"Unique serialized network prefab ID");
            }
        }
        for(int q=0;q<5;q++) for(int e=0;e<5;e++) for(int r=0;r<5;r++)
        {
            var set=new ElementLoadout{q=(MagicElement)q,e=(MagicElement)e,r=(MagicElement)r};
            check(set.IsValid==(q!=e&&q!=r&&e!=r),"Loadout uniqueness");
            if(!set.IsValid)continue;
            foreach(var spell in manager.Spells)
                check(spell.IsAvailable(set)==spell.Recipe.All(set.Contains),"Catalog filtering");
        }
        var appearance=player.GetComponentInChildren<WizardAppearance>(true);
        check(appearance!=null && appearance.pieces.Length==4,"Four death pieces");
        foreach(var piece in appearance.pieces)
            check(piece!=null && piece.GetComponentsInChildren<MeshRenderer>().Length>0,"Piece has geometry");
        var movement=new SerializedObject(player.GetComponentInChildren<RelativeMovement>(true));
        check(movement.FindProperty("jumpSpeed").floatValue==12,"Original jump speed preserved");
        check(movement.FindProperty("terminalVelocity").floatValue==-22,"Controlled falling cap");
        check(movement.FindProperty("fallGravityMultiplier").floatValue==1.8f,"Falling acceleration");
        Debug.Log($"EXPANSION_VALIDATION_PASSED: {checks} checks");
    }
    // создаём временную сцену и сохраняем изображение модели мага для визуальной проверки.
    public static void RenderWizard()
    {
        bool batching=UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching;
        UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching=false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight=Color.white*.6f;RenderSettings.sun=null;RenderSettings.skybox=null;
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var source=player.GetComponentInChildren<WizardAppearance>(true).visualRoot;
        var model=UnityEngine.Object.Instantiate(source.gameObject);
        model.transform.position=Vector3.zero;
        var renderers=model.GetComponentsInChildren<Renderer>();
        Bounds b=renderers[0].bounds; foreach(var r in renderers)b.Encapsulate(r.bounds);
        foreach(var renderer in renderers)
            Debug.Log("WIZARD_MATERIAL "+renderer.name+" "+string.Join(";",renderer.sharedMaterials.Select(m=>m.name+"="+m.GetColor("_BaseColor"))));
        var camera=new GameObject("Wizard camera").AddComponent<Camera>();
        camera.fieldOfView=35;
        camera.transform.position=b.center+new Vector3(1.5f,1,-5).normalized*b.size.y*2.3f;
        camera.transform.LookAt(b.center);
        camera.nearClipPlane=.01f;camera.farClipPlane=100;
        camera.backgroundColor=new Color(.08f,.10f,.16f);
        camera.clearFlags=CameraClearFlags.SolidColor;
        var light=new GameObject("White key light").AddComponent<Light>();
        light.type=LightType.Directional;light.color=Color.white;light.intensity=1.3f;
        light.transform.rotation=Quaternion.Euler(40,-30,0);
        var render=new RenderTexture(640,720,24);camera.targetTexture=render;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=render;
        var texture=new Texture2D(640,720,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,640,720),0,0);texture.Apply();
        System.IO.File.WriteAllBytes("../wizard-preview.png",texture.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(render);
        UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
        Debug.Log("WIZARD_PREVIEW_SAVED");
        UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching=batching;
    }
    // заново создаём базовое расширение, проверяем его и при наличии графики сохраняем изображение мага.
    public static void BuildAndValidate()
    {
        WizardExpansionBuilder.Build();
        Run();
        SpellSystemValidation.Run();
        if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)RenderWizard();
    }
    // обновляем сетевые идентификаторы сохранённых префабов и запускаем проверки исходного расширения.
    public static void FinalizeAndValidate()
    {
        WizardExpansionBuilder.FinalizeNetworkPrefabs();
        Run();
        SpellSystemValidation.Run();
    }
}
