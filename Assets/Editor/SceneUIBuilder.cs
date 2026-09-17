using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public static class SceneUIBuilder
{
    [MenuItem("Tools/Wizard War/Install editable scene UI")]
    public static void Install()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        var menu=EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
        if(Object.FindFirstObjectByType<ElementLoadoutUI>()==null)
        {
            var root=new GameObject("Spellbook UI");
            root.AddComponent<ElementLoadoutUI>().EditorBake(Catalog());
            SetLayer(root);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,"Assets/Prefabs/UI/SpellbookUI.prefab",InteractionMode.AutomatedAction);
        }
        EnsureEventSystem();EditorSceneManager.SaveScene(menu);
        var arena=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        if(Object.FindFirstObjectByType<PlayerGameUI>()==null)
        {
            const string spritePath="Assets/Prefabs/UI/CooldownMask.png";
            if(!System.IO.File.Exists(spritePath))
            {
                var texture=new Texture2D(2,2);texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply();
                System.IO.File.WriteAllBytes(spritePath,texture.EncodeToPNG());Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(spritePath);
            }
            var importer=(TextureImporter)AssetImporter.GetAtPath(spritePath);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.SaveAndReimport();
            var mask=AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if(mask==null)throw new System.InvalidOperationException("Cooldown mask sprite failed to import");
            var root=new GameObject("Match UI");
            root.AddComponent<PlayerGameUI>().EditorBake(Catalog(),mask);
            SetLayer(root);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,"Assets/Prefabs/UI/MatchUI.prefab",InteractionMode.AutomatedAction);
        }
        EnsureEventSystem();EditorSceneManager.SaveScene(arena);
        AssetDatabase.SaveAssets();
        Debug.Log("SCENE_UI_INSTALLED");
    }
    static SpellManager Catalog()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<SpellManager>(true);
        if(catalog==null||catalog.Spells.Count==0)throw new System.InvalidOperationException("Player spell catalog is empty");
        return catalog;
    }
    static void SetLayer(GameObject root){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;}
    static void EnsureEventSystem()
    {
        if(Object.FindFirstObjectByType<EventSystem>()!=null)return;
        var go=new GameObject("EventSystem",typeof(EventSystem));
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
