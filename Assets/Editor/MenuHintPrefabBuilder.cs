using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// создаёт самостоятельные префабы и один раз размещает их возле моделей, без связи с коллайдерами.
public static class MenuHintPrefabBuilder
{
    private const string Folder = "Assets/Prefabs/MenuHighlights";

    [MenuItem("Tools/Wizard War/Install highlight prefabs")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var menu = objects.Select(t => t.GetComponent<InteractiveShelfMenu>()).Single(c => c != null);
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        string materialPath = Folder + "/InteractionGlow.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Wizard/MenuInteractionGlow"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var data = new SerializedObject(menu);
        // модели нужны лишь для начальной расстановки; после сохранения размеры полностью независимы.
        Bounds shelf = ModelBounds(objects.Single(t => t.name == "Shelf (1)"));
        Bounds hat = ModelBounds(objects.Single(t => t.name == "WizardHat"));
        Bounds portal = ModelBounds(objects.Single(t => t.name == "Portal"));
        Place(data, "shelfHint", "Shelf Highlight", shelf, false, false, menu, material);
        Place(data, "hatHint", "Hat Highlight", hat, true, false, menu, material);
        Place(data, "portalHint", "Portal Highlight", portal, false, true, menu, material);
        data.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/menu-highlight-prefabs.txt", "PASS: three saved prefab instances; local-space lines; serialized references; no collider dependency.");
    }

    private static Bounds ModelBounds(Transform model)
    {
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("Missing model renderer: " + model.name);
        Bounds result = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) result.Encapsulate(renderer.bounds);
        return result;
    }

    // повторный запуск не трогает уже назначенные пользователем объекты и их размеры.
    private static void Place(SerializedObject data, string field, string name, Bounds bounds, bool ring, bool arch,
        InteractiveShelfMenu menu, Material material)
    {
        if (data.FindProperty(field).objectReferenceValue != null) return;
        string path = Folder + "/" + name + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            var root = new GameObject(name);
            try
            {
                Vector3[] points = Points(ring, arch);
                var glow = MakeLine(root.transform, "Soft glow", points, material);
                var core = MakeLine(root.transform, "Fine edge", points, material);
                root.AddComponent<MenuInteractionHint>().Configure(glow, core);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, menu.transform);
        Undo.RegisterCreatedObjectUndo(instance, "Place highlight prefab");
        if (ring)
        {
            instance.transform.position = new Vector3(bounds.center.x, bounds.min.y + .025f, bounds.center.z);
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = new Vector3(bounds.size.x + .05f, 1, bounds.size.z + .05f);
        }
        else
        {
            Vector3 normal = bounds.extents.x < bounds.extents.z ? Vector3.right : Vector3.forward;
            if (Vector3.Dot(normal, menu.MenuCamera.transform.position - bounds.center) < 0) normal = -normal;
            float depth = Mathf.Abs(normal.x) * bounds.extents.x + Mathf.Abs(normal.z) * bounds.extents.z;
            float width = Mathf.Abs(normal.z) * bounds.size.x + Mathf.Abs(normal.x) * bounds.size.z;
            instance.transform.position = bounds.center + normal * (depth + .03f);
            instance.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
            instance.transform.localScale = new Vector3(width + .04f, bounds.size.y + .04f, 1);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        data.FindProperty(field).objectReferenceValue = instance.GetComponent<MenuInteractionHint>();
        if (instance.GetComponentsInChildren<LineRenderer>().Any(line => line.useWorldSpace))
            throw new InvalidOperationException("Highlight lines must use local coordinates.");
    }

    // точки хранятся внутри префаба в локальных координатах, поэтому обычный scale меняет весь контур.
    private static Vector3[] Points(bool ring, bool arch)
    {
        if (ring)
            return Enumerable.Range(0, 64).Select(i => new Vector3(Mathf.Cos(i * Mathf.PI / 32) * .5f, 0,
                Mathf.Sin(i * Mathf.PI / 32) * .5f)).ToArray();
        if (!arch) return new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
        var points = new Vector3[35];
        points[0] = new Vector3(-.5f, -.5f, 0);
        for (int i = 0; i <= 32; i++)
        {
            float angle = Mathf.PI - i * Mathf.PI / 32;
            points[i + 1] = new Vector3(Mathf.Cos(angle) * .5f, .15f + Mathf.Sin(angle) * .35f, 0);
        }
        points[34] = new Vector3(.5f, -.5f, 0);
        return points;
    }

    private static LineRenderer MakeLine(Transform parent, string name, Vector3[] points, Material material)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var line = root.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.numCornerVertices = 3;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return line;
    }
}
