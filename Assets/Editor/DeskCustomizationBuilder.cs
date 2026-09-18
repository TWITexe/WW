using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// переносит существующее окно настройки на планшет и подключает настоящую шляпу к камере меню.
public static class DeskCustomizationBuilder
{
    // сохраняем все поля, цвета и обработчики исходного интерфейса, меняя только его размещение и выход.
    [MenuItem("Tools/Wizard War/Install desk customization")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        Transform[] objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var menu = objects.Select(item => item.GetComponent<InteractiveShelfMenu>()).Single(item => item != null);
        if (menu.CustomizationDesk != null) throw new InvalidOperationException("Desk already installed; edit CanvasForCastom in the Inspector.");
        Canvas canvas = objects.Single(item => item.name == "CanvasForCastom").GetComponent<Canvas>();
        Transform board = objects.Single(item => item.name == "Desk (1)");
        Transform hat = objects.Single(item => item.name == "WizardHat");
        Button exit = canvas.GetComponentsInChildren<Button>(true).Single(item => item.name == "ButtonExit");
        Directory.CreateDirectory("Logs/InteractiveShelfMenu");
        EditorSceneManager.SaveScene(scene);
        File.Copy(scene.path, "Logs/InteractiveShelfMenu/Menu-before-desk-customization.unity", true);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Move customization to desk");

        // область нажатия следует за шляпой, даже если модель позже передвинут в редакторе.
        var hitObject = new GameObject("Customization hat hit area");
        hitObject.transform.SetParent(hat, false);
        Undo.RegisterCreatedObjectUndo(hitObject, "Create hat hit area");
        var hit = hitObject.AddComponent<BoxCollider>();
        Bounds hatBounds = hat.GetComponent<MeshFilter>().sharedMesh.bounds;
        hit.center = hatBounds.center;
        hit.size = hatBounds.size;
        hit.isTrigger = true;

        // наклон совпадает с лицевой плоскостью деревянного планшета в исходной модели стола.
        RectTransform rect = (RectTransform)canvas.transform;
        Undo.SetTransformParent(rect, board, "Attach customization to desk");
        Undo.RecordObject(rect, "Place customization on board");
        Vector3 normal = new Vector3(0, .50155f, .86513f);
        Vector3 up = new Vector3(0, .86513f, -.50155f);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(900, 740);
        rect.localPosition = new Vector3(0, 1.19584f, .00615f) + normal * .008f + up * .025f;
        rect.localRotation = Quaternion.LookRotation(-normal, up);
        rect.localScale = new Vector3(.001f, .001f / board.TransformVector(up).magnitude, .001f);
        Undo.RecordObject(canvas, "Use world-space canvas");
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = menu.MenuCamera;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        Undo.RecordObject(scaler, "Set world-space scale");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1;
        scaler.dynamicPixelsPerUnit = 2;
        Undo.RecordObject(canvas.gameObject, "Keep desk UI visible");
        canvas.gameObject.SetActive(true);
        var group = Undo.AddComponent<CanvasGroup>(canvas.gameObject);
        group.alpha = 1;
        var desk = Undo.AddComponent<DeskCustomizationUI>(canvas.gameObject);
        desk.Configure(group, canvas.GetComponent<GraphicRaycaster>(), hit);
        Undo.RecordObject(menu, "Connect desk camera destination");
        menu.ConfigureDesk(desk);

        // убираем только обработчик закрытия: теперь выход меняет ракурс, а холст остаётся на планшете.
        Undo.RecordObject(exit, "Return camera from desk");
        for (int index = exit.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
            UnityEventTools.RemovePersistentListener(exit.onClick, index);
        UnityEventTools.AddPersistentListener(exit.onClick, menu.ReturnToOverview);

        // прежняя экранная кнопка шляпы больше не нужна: окно открывает сама модель на столе.
        foreach (Button button in objects.Select(item => item.GetComponent<Button>()).Where(item => item != null))
        {
            if (button.transform.IsChildOf(canvas.transform)) continue;
            var data = new SerializedObject(button);
            var calls = data.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int index = 0; index < calls.arraySize; index++)
            {
                var call = calls.GetArrayElementAtIndex(index);
                if (call.FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue != canvas.gameObject) continue;
                Undo.RecordObject(button.gameObject, "Hide old customization shortcut");
                button.gameObject.SetActive(false);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Validate();
        MenuShelfInspection.Inspect();
    }

    // оставляем место над нижней деревянной планкой, чтобы она не закрывала кнопку выхода.
    [MenuItem("Tools/Wizard War/Fit customization to desk")]
    public static void FitCanvas()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        var desk = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<DeskCustomizationUI>(true)).Single();
        RectTransform rect = (RectTransform)desk.transform;
        Undo.RecordObject(rect, "Fit UI above desk ledge");
        Vector3 normal = new Vector3(0, .50155f, .86513f);
        Vector3 up = new Vector3(0, .86513f, -.50155f);
        rect.localPosition = new Vector3(0, 1.19584f, .00615f) + normal * .008f + up * .025f;
        rect.localScale = new Vector3(.001f, .001f / rect.parent.TransformVector(up).magnitude, .001f);
        EditorSceneManager.MarkSceneDirty(desk.gameObject.scene);
        EditorSceneManager.SaveScene(desk.gameObject.scene);
        MenuShelfInspection.Inspect();
        Validate();
    }

    // проверяем постоянную видимость, блокировку ввода и попадание луча в шляпу из общего ракурса.
    [MenuItem("Tools/Wizard War/Validate desk customization")]
    public static void Validate()
    {
        var menu = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<InteractiveShelfMenu>(true)).Single();
        DeskCustomizationUI desk = menu.CustomizationDesk;
        if (desk == null || !desk.gameObject.activeInHierarchy) throw new InvalidOperationException("Desk UI must remain visible.");
        Canvas canvas = desk.GetComponent<Canvas>();
        if (canvas.renderMode != RenderMode.WorldSpace || canvas.worldCamera != menu.MenuCamera)
            throw new InvalidOperationException("Desk canvas must use menu camera in world space.");
        Physics.SyncTransforms();
        Vector3 origin = new Vector3(-5.54f, 1.66f, -6.94f);
        if (!desk.HatHitArea.Raycast(new Ray(origin, desk.HatHitArea.bounds.center - origin), out _, 30))
            throw new InvalidOperationException("Hat is not clickable from overview.");
        if (desk.IsInteractive) throw new InvalidOperationException("Desk should start with input disabled.");
        // переключение доступа не должно выключать планшет или менять его прозрачность.
        try
        {
            desk.SetInteraction(true);
            if (!desk.IsInteractive || !desk.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Desk must accept input when focused.");
        }
        finally { desk.SetInteraction(false); }
        if (desk.GetComponent<CanvasGroup>().alpha != 1 || !desk.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Unfocused desk must remain fully visible.");
        if (desk.GetComponentsInChildren<ColorButton>(true).Length != 10 || desk.GetComponentInChildren<TMPro.TMP_InputField>(true) == null)
            throw new InvalidOperationException("Original customization controls are incomplete.");
        File.WriteAllText("Logs/desk-customization-validation.txt", "PASS: visible world-space UI; input disabled before flight; clickable hat; nickname and ten colors preserved.");
    }
}
