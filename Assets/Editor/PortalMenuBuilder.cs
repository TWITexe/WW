using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// подключает портал к существующему окну connectui, сохраняя сетевые кнопки и структуру canvasmain.
public static class PortalMenuBuilder
{
    // устанавливаем область нажатия и анимацию окна в текущую сцену меню.
    [MenuItem("Tools/Wizard War/Install portal connection menu")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        Transform[] objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var menu = objects.Select(item => item.GetComponent<InteractiveShelfMenu>()).Single(item => item != null);
        if (menu.PortalMenu != null) throw new InvalidOperationException("Portal menu already installed.");
        Transform portal = objects.Single(item => item.name == "Portal");
        GameObject window = objects.Single(item => item.name == "ConnectUI").gameObject;
        Canvas canvas = window.GetComponentInParent<Canvas>(true);
        if (canvas == null || canvas.name != "CanvasMain") throw new InvalidOperationException("ConnectUI must remain inside CanvasMain.");
        Button exit = window.GetComponentsInChildren<Button>(true).Single(item => item.name == "ButtonExit");
        Directory.CreateDirectory("Logs/InteractiveShelfMenu");
        EditorSceneManager.SaveScene(scene);
        File.Copy(scene.path, "Logs/InteractiveShelfMenu/Menu-before-portal.unity", true);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect portal to connection menu");

        // используем мировые размеры: у декоративного портала отрицательный масштаб, непригодный для boxcollider.
        Renderer[] renderers = portal.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        var hitObject = new GameObject("Portal connection hit area");
        hitObject.transform.SetParent(menu.transform, false);
        hitObject.transform.position = bounds.center;
        Undo.RegisterCreatedObjectUndo(hitObject, "Create portal hit area");
        var hit = hitObject.AddComponent<BoxCollider>();
        hit.size = bounds.size;
        hit.isTrigger = true;
        var portalMenu = Undo.AddComponent<PortalConnectMenu>(hitObject);
        var group = window.GetComponent<CanvasGroup>();
        if (group == null) group = Undo.AddComponent<CanvasGroup>(window);
        Undo.RecordObject(group, "Configure connection fade");
        Undo.RecordObject(window, "Hide connection window until arrival");
        portalMenu.Configure(hit, group);

        // canvasmain нужен окну подключения, поэтому вместо всего холста скрываем его остальные дочерние объекты.
        var menuData = new SerializedObject(menu);
        var oldObjects = menuData.FindProperty("overviewOnlyObjects");
        var overview = new List<GameObject>();
        for (int index = 0; index < oldObjects.arraySize; index++)
        {
            var item = oldObjects.GetArrayElementAtIndex(index).objectReferenceValue as GameObject;
            if (item != null && item != canvas.gameObject && item != window) overview.Add(item);
        }
        foreach (Transform child in canvas.transform)
            if (child.gameObject != window && !overview.Contains(child.gameObject)) overview.Add(child.gameObject);
        Undo.RecordObject(menu, "Connect portal and preserve canvas");
        menu.ConfigurePortal(portalMenu, overview.ToArray());
        Undo.RecordObject(canvas.gameObject, "Keep main canvas available");
        canvas.gameObject.SetActive(true);

        // выход закрывает подключение и возвращает общий ракурс тем же контроллером камеры.
        Undo.RecordObject(exit, "Return from portal");
        for (int index = exit.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
            UnityEventTools.RemovePersistentListener(exit.onClick, index);
        UnityEventTools.AddPersistentListener(exit.onClick, menu.ReturnToOverview);
        foreach (Button button in canvas.GetComponentsInChildren<Button>(true))
        {
            if (button.transform.IsChildOf(window.transform)) continue;
            var data = new SerializedObject(button);
            var calls = data.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int index = 0; index < calls.arraySize; index++)
                if (calls.GetArrayElementAtIndex(index).FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue == window)
                {
                    Undo.RecordObject(button.gameObject, "Hide old connection shortcut");
                    button.gameObject.SetActive(false);
                }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Validate();
    }

    // проверяем попадание в портал, блокировку ввода во время появления и сохранённые сетевые обработчики.
    [MenuItem("Tools/Wizard War/Validate portal connection menu")]
    public static void Validate()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Validate outside Play Mode.");
        var menu = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<InteractiveShelfMenu>(true)).Single();
        PortalConnectMenu portal = menu.PortalMenu;
        if (portal == null) throw new InvalidOperationException("Portal references are missing.");
        CanvasGroup panel = portal.Panel;
        if (panel.transform.parent.name != "CanvasMain" || !panel.transform.parent.gameObject.activeInHierarchy)
            throw new InvalidOperationException("ConnectUI must retain its active CanvasMain parent.");
        Physics.SyncTransforms();
        Vector3 origin = new Vector3(-5.54f, 1.66f, -6.94f);
        if (!portal.HitArea.Raycast(new Ray(origin, portal.HitArea.bounds.center - origin), out _, 30))
            throw new InvalidOperationException("Portal is not clickable from overview.");
        var hits = new List<RaycastResult>();
        if (EventSystem.current != null)
        {
            var pointer = new PointerEventData(EventSystem.current) { position = menu.MenuCamera.WorldToScreenPoint(portal.HitArea.bounds.center) };
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count > 0) throw new InvalidOperationException("UI blocks portal: " + hits[0].gameObject.name);
        }
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            if ((button.name == "ButtonConnect" || button.name == "ButtonHost" || button.name == "ButtonServer" || button.name == "ButtonRefresh")
                && button.onClick.GetPersistentEventCount() == 0)
                throw new InvalidOperationException("Missing original handler: " + button.name);
        try
        {
            portal.Show();
            if (panel.alpha != 0 || panel.interactable || panel.blocksRaycasts)
                throw new InvalidOperationException("Window must start transparent and inactive.");
            portal.UpdateAppearance(.1f);
            if (panel.alpha <= 0 || panel.alpha >= 1 || panel.interactable)
                throw new InvalidOperationException("Window must fade gradually before accepting input.");
            portal.UpdateAppearance(1);
            if (panel.alpha != 1 || !panel.interactable || !panel.blocksRaycasts)
                throw new InvalidOperationException("Window must accept input after fading in.");
        }
        finally { portal.Hide(); }
        File.WriteAllText("Logs/portal-menu-validation.txt", "PASS: portal raycast and UI blockers; ConnectUI parent preserved; gradual fade and input gating; original connection handlers retained.");
        CapturePreview(menu);
    }

    // для снимка временно рисуем экранный холст камерой и затем возвращаем исходный режим overlay.
    private static void CapturePreview(InteractiveShelfMenu menu)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        Camera camera = menu.MenuCamera;
        Canvas canvas = menu.PortalMenu.Panel.GetComponentInParent<Canvas>();
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderMode mode = canvas.renderMode;
        Camera previousCamera = canvas.worldCamera;
        float distance = canvas.planeDistance;
        var target = new RenderTexture(1280, 720, 24);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.transform.SetPositionAndRotation(new Vector3(-3.16f, .97f, -11.88f), Quaternion.Euler(8.23f, 180, 0));
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = .5f;
            menu.PortalMenu.Show();
            menu.PortalMenu.UpdateAppearance(1);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes("Logs/InteractiveShelfMenu/portal.png", image.EncodeToPNG());
        }
        finally
        {
            menu.PortalMenu.Hide();
            canvas.renderMode = mode;
            canvas.worldCamera = previousCamera;
            canvas.planeDistance = distance;
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
