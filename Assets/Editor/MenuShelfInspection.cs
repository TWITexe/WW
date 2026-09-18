using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// собирает сведения о полках текущей сцены для настройки пространственного меню.
[InitializeOnLoad]
public static class MenuShelfInspection
{
    private const string RequestPath = "Logs/menu-shelf-request.txt";
    private static double nextCheck;

    // проверяем только явный локальный запрос, не меняя сцену при обычном открытии редактора.
    static MenuShelfInspection()
    {
        EditorApplication.update += ProcessRequest;
    }

    // выполняем одну из заранее определённых команд настройки и удаляем обработанный запрос.
    private static void ProcessRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        string request = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        try
        {
            if (request == "validate-spells") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate spell system");
            else if (request == "taller-tornado") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Enlarge fire tornado");
            else if (request == "impact-regression") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate environment impacts");
            else if (request == "area-hits") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Install area and hit effects");
            else if (request == "validate-area-hits") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate area and hit effects");
            else if (request == "inspect") Inspect();
            else if (request == "refresh") AssetDatabase.Refresh();
            else if (request == "optimize") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Optimize menu lighting");
            else if (request == "install") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Install interactive shelves");
            else if (request == "validate") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate interactive shelves");
            else if (request == "slots") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Install open book slots");
            else if (request == "validate-slots") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate open book slots");
            else if (request == "cover-symbols") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Use cover symbols on open books");
            else if (request == "desk") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Install desk customization");
            else if (request == "validate-desk") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate desk customization");
            else if (request == "fit-desk") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Fit customization to desk");
            else if (request == "color-marks") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Add color selection marks");
            else if (request == "portal") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Install portal connection menu");
            else if (request == "validate-portal") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate portal connection menu");
            else if (request == "inspect-fire") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Inspect fire seal circle");
            else if (request == "fire-circle") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Install fire seal circle");
            else if (request == "validate-fire") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate fire seal circle");
            else if (request == "ice-shield") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Install ice and shield effects");
            else if (request == "validate-ice-shield") EditorApplication.ExecuteMenuItem("Tools/Wizard War/Validate ice and shield effects");
        }
        catch (System.Exception exception)
        {
            File.WriteAllText("Logs/menu-shelf-error.txt", exception.ToString());
            Debug.LogException(exception);
        }
    }

    // сохраняем пути, координаты и материалы живой сцены, включая ещё не сохранённые правки.
    [MenuItem("Tools/Wizard War/Inspect menu shelves")]
    public static void Inspect()
    {
        var report = new StringBuilder();
        Scene scene = SceneManager.GetActiveScene();
        report.AppendLine($"SCENE {scene.path} dirty={scene.isDirty} playing={Application.isPlaying}");
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"CAMERA {PathOf(camera.transform)} pos={camera.transform.position:F4} rot={camera.transform.eulerAngles:F4} fov={camera.fieldOfView}");
        foreach (var item in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)))
        {
            if (!(item.name.ToLowerInvariant().Contains("book") || item.name.ToLowerInvariant().Contains("shelf"))) continue;
            report.AppendLine($"OBJECT {PathOf(item)} pos={item.position:F4} rot={item.eulerAngles:F4} scale={item.lossyScale:F4} active={item.gameObject.activeInHierarchy}");
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                report.AppendLine($"  RENDERER {PathOf(renderer.transform)} bounds={renderer.bounds} materials={string.Join(",", renderer.sharedMaterials.Select(m => m != null ? m.name : "null"))}");
        }
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"CANVAS {PathOf(canvas.transform)} mode={canvas.renderMode} active={canvas.gameObject.activeInHierarchy}");
        // учитываем переименованные модели возле полок, чтобы найти открытые книги по геометрии.
        foreach (var model in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)))
        {
            if (Vector3.Distance(model.transform.position, new Vector3(-2, 1, -7.5f)) > 2.5f) continue;
            report.AppendLine($"NEAR {PathOf(model.transform)} mesh={model.sharedMesh.name} pos={model.transform.position:F4} rot={model.transform.eulerAngles:F4} scale={model.transform.lossyScale:F4} bounds={model.sharedMesh.bounds}");
        }
        File.WriteAllText("Logs/menu-shelf-inspection.txt", report.ToString());
        // описываем предметы у письменного стола и исходную вёрстку окна настройки персонажа.
        var desk = new StringBuilder();
        foreach (var item in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)))
        {
            if (item.TryGetComponent<Renderer>(out var renderer) && (Vector3.Distance(item.position, new Vector3(-6, 1, -8)) < 4 || PathOf(item).Contains("Portal")))
                desk.AppendLine($"MODEL {PathOf(item)} pos={item.position:F4} rot={item.eulerAngles:F4} scale={item.lossyScale:F4} bounds={renderer.bounds}");
            if (PathOf(item).Contains("CanvasForCastom") || PathOf(item).Contains("CanvasMain"))
            {
                desk.AppendLine($"UI {PathOf(item)} active={item.gameObject.activeSelf} components={string.Join(",", item.GetComponents<Component>().Select(component => component.GetType().Name))}");
                if (item is RectTransform rect)
                    desk.AppendLine($"  RECT pos={rect.anchoredPosition} size={rect.sizeDelta} anchors={rect.anchorMin}/{rect.anchorMax} pivot={rect.pivot}");
            }
        }
        File.WriteAllText("Logs/menu-desk-inspection.txt", desk.ToString());
        // сохраняем ракурс письменного стола для точного размещения интерфейса на его наклонной поверхности.
        var deskCamera = Camera.main;
        var oldPosition = deskCamera.transform.position;
        var oldRotation = deskCamera.transform.rotation;
        var oldTarget = deskCamera.targetTexture;
        var oldActive = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            deskCamera.transform.SetPositionAndRotation(new Vector3(-4.65f, 1.32f, -7.8f), Quaternion.Euler(26.82f, -90.7f, 0));
            deskCamera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            deskCamera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes("Logs/InteractiveShelfMenu/desk.png", image.EncodeToPNG());
        }
        finally
        {
            deskCamera.transform.SetPositionAndRotation(oldPosition, oldRotation);
            deskCamera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
        }
        var deskModel = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true))
            .Single(item => item.name == "Desk (1)");
        var points = deskModel.sharedMesh.vertices;
        var normals = deskModel.sharedMesh.normals;
        File.WriteAllLines("Logs/desk-vertices.txt", points.Select((point, index) => $"{point:F5} normal={normals[index]:F5}"));
    }

    // составляем однозначный путь объекта через его родителей в иерархии.
    private static string PathOf(Transform item)
    {
        return item.parent == null ? item.name : PathOf(item.parent) + "/" + item.name;
    }
}
