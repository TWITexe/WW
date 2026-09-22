using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// снимает подсказки в копии сохранённой сцены, не затрагивая расстановку открытого меню.
public static class MenuInteractionHintPreview
{
    [MenuItem("Tools/Wizard War/Preview interaction hints")]
    public static void Capture()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Menu.unity");
        var target = new RenderTexture(1200, 700, 24);
        var image = new Texture2D(1200, 700, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        Material[] materials = null;
        try
        {
            var menu = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<InteractiveShelfMenu>(true)).Single();
            var data = new SerializedObject(menu);
            Camera camera = menu.MenuCamera;
            camera.scene = scene;
            camera.transform.SetPositionAndRotation(data.FindProperty("overviewPosition").vector3Value,
                Quaternion.Euler(data.FindProperty("overviewAngles").vector3Value));
            foreach (var hint in menu.GetComponentsInChildren<MenuInteractionHint>(true)) hint.Show(false);
            materials = menu.GetComponentsInChildren<LineRenderer>().Select(line => line.sharedMaterial).Distinct().ToArray();
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1200, 700), 0, 0);
            image.Apply();
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/menu-interaction-hints.png", image.EncodeToPNG());
            camera.targetTexture = null;
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            if (materials != null)
                foreach (var material in materials)
                    if (material != null && !AssetDatabase.Contains(material)) Object.DestroyImmediate(material);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }
}
