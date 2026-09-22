using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// читаем реальные размеры сцены и моделей перед сборкой новой арены.
public static class ArenaInspection
{
    [MenuItem("Tools/Wizard War/Inspect arena")]
    public static void Run()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("нужно выйти из play mode");
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        var text = new StringBuilder();
        text.AppendLine("dirty="+scene.isDirty);
        foreach (var root in scene.GetRootGameObjects()) Walk(root.transform, text, 0);
        text.AppendLine("PREFABS");
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Other Asstets/LowPolyMedievalStarterPack/Prefabs"}))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = Object.Instantiate(prefab);
            text.AppendLine(path + " " + Bounds(instance));
            Object.DestroyImmediate(instance);
        }
        File.WriteAllText("Logs/arena-inspection.txt", text.ToString());
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }
    static string Bounds(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return "";
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return "bounds=" + bounds + " materials=" + string.Join(",", System.Array.ConvertAll(renderers[0].sharedMaterials,m=>m == null ? "null" : m.name+":"+m.shader.name));
    }
    static void Walk(Transform t, StringBuilder text, int depth)
    {
        if (depth > 2) return;
        text.AppendLine(new string(' ',depth*2)+t.name+" active="+t.gameObject.activeSelf+" pos="+t.position+" scale="+t.localScale+" "+Bounds(t.gameObject)+" components="+string.Join(",",System.Array.ConvertAll(t.GetComponents<Component>(),c=>c==null?"missing":c.GetType().Name)));
        foreach(Transform child in t) Walk(child,text,depth+1);
    }
}
