using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// переносит выбор слотов на существующие развороты, не меняя авторскую расстановку моделей.
public static class OpenBookSlotBuilder
{
    // устанавливаем страницы в живую сцену и сохраняем резервную копию до изменений.
    [MenuItem("Tools/Wizard War/Install open book slots")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        Transform[] objects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        InteractiveShelfMenu menu = objects.Select(item => item.GetComponent<InteractiveShelfMenu>())
            .Single(item => item != null);
        if (menu.OpenBookSlots.Length != 0)
            throw new InvalidOperationException("Open book slots already installed; edit their page transforms in the Inspector.");
        Transform[] models = objects.Where(item => item.TryGetComponent<MeshFilter>(out var filter)
            && filter.sharedMesh != null && filter.sharedMesh.name == "Book_Open"
            && Vector3.Distance(item.position, new Vector3(-2, 1.26f, -7.54f)) < 1.2f)
            .OrderByDescending(item => item.position.z).ToArray();
        if (models.Length != 3) throw new InvalidOperationException("Expected three open books above the elements.");
        Spell[] spells = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab")
            .GetComponentInChildren<SpellManager>(true).Spells.ToArray();
        string[] symbolNames = { "FireBall", "WindFlow", "IceShard", "Boulder", "WaterBolt" };
        Spell[] symbols = symbolNames.Select(name => spells.Single(spell => spell != null && spell.name == name)).ToArray();

        Directory.CreateDirectory("Logs/InteractiveShelfMenu");
        EditorSceneManager.SaveScene(scene);
        File.Copy(scene.path, "Logs/InteractiveShelfMenu/Menu-before-open-book-slots.unity", true);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Move element slots to open books");
        var slots = new OpenBookSlot[3];
        for (int index = 0; index < models.Length; index++)
            slots[index] = BuildSlot(models[index], menu.MenuCamera, index, symbols);
        Undo.RecordObject(menu, "Connect open books");
        menu.ConfigureSlotBooks(slots);
        UpdateCatalog(menu.Catalog);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Validate();
        InteractiveShelfMenuBuilder.Validate();
    }

    // обе страницы следуют за моделью книги; коллайдер покрывает весь разворот, включая поля.
    private static OpenBookSlot BuildSlot(Transform model, Camera camera, int index, Spell[] symbols)
    {
        var root = new GameObject("Open book slot " + "QER"[index]);
        root.transform.SetParent(model, false);
        Undo.RegisterCreatedObjectUndo(root, "Create open book slot");
        var slot = root.AddComponent<OpenBookSlot>();
        var hit = root.AddComponent<BoxCollider>();
        Bounds bounds = model.GetComponent<MeshFilter>().sharedMesh.bounds;
        hit.center = bounds.center;
        hit.size = bounds.size + Vector3.one * .015f;
        hit.isTrigger = true;
        var content = new GameObject("Page content");
        content.transform.SetParent(root.transform, false);

        // локальная положительная ось x этой модели соответствует левой странице в ракурсе камеры.
        Transform left = CreatePage(content.transform, camera, "Left page - key", .17f);
        var keyObject = new GameObject("Key", typeof(RectTransform), typeof(Text));
        keyObject.transform.SetParent(left, false);
        Text key = keyObject.GetComponent<Text>();
        key.rectTransform.sizeDelta = new Vector2(150, 170);
        key.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        key.fontSize = 110;
        key.fontStyle = FontStyle.Bold;
        key.alignment = TextAnchor.MiddleCenter;
        key.text = "QER"[index].ToString();
        key.raycastTarget = false;
        var markObject = new GameObject("Selected slot underline", typeof(RectTransform), typeof(Image));
        markObject.transform.SetParent(left, false);
        Image mark = markObject.GetComponent<Image>();
        mark.rectTransform.sizeDelta = new Vector2(90, 7);
        mark.rectTransform.anchoredPosition = new Vector2(0, -66);
        mark.color = new Color(.65f, .29f, .045f);
        mark.raycastTarget = false;

        Transform right = CreatePage(content.transform, camera, "Right page - element", -.17f);
        var iconObject = new GameObject("Element symbol", typeof(RectTransform), typeof(SpellIconGraphic));
        iconObject.transform.SetParent(right, false);
        var icon = iconObject.GetComponent<SpellIconGraphic>();
        icon.rectTransform.sizeDelta = new Vector2(140, 140);
        icon.raycastTarget = false;
        // тёмная обводка сохраняет читаемость светлых стихий на пергаменте.
        var outline = iconObject.AddComponent<Outline>();
        outline.effectColor = new Color(.12f, .065f, .025f, .9f);
        outline.effectDistance = new Vector2(2, -2);
        slot.Configure(index, hit, content, key, mark, icon, symbols);
        slot.Refresh(ElementLoadout.Default, 0);
        slot.SetVisible(false);
        return slot;
    }

    // располагаем прозрачный холст чуть выше бумаги и вдоль наклона внешней части страницы.
    private static Transform CreatePage(Transform parent, Camera camera, string name, float x)
    {
        var page = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        page.transform.SetParent(parent, false);
        var rect = page.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(170, 220);
        rect.localPosition = new Vector3(x, .081f, 0);
        Vector3 normal = new Vector3(Mathf.Sign(x) * .11f, .994f, 0).normalized;
        rect.localRotation = Quaternion.LookRotation(-normal, Vector3.back);
        rect.localScale = Vector3.one * .001f;
        Canvas canvas = page.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        return page.transform;
    }

    // освобождаем верх каталога под список и сохраняем тёмно-коричневый фон всех карточек.
    private static void UpdateCatalog(ShelfSpellCatalogUI catalog)
    {
        Undo.RecordObject(catalog, "Detach old slot controls");
        catalog.DetachSlotControls();
        foreach (RectTransform item in catalog.GetComponentsInChildren<RectTransform>(true))
        {
            if (item.name == "Loadout caption" || item.name == "Slot 0" || item.name == "Slot 1" || item.name == "Slot 2")
            {
                Undo.RecordObject(item.gameObject, "Hide old slot control");
                item.gameObject.SetActive(false);
            }
            if (item.name == "Scroll viewport")
            {
                Undo.RecordObject(item, "Expand spell list");
                item.anchoredPosition = new Vector2(20, -76);
                item.sizeDelta = new Vector2(680, 588);
            }
            if (item.parent != null && item.parent.name == "Spell cards" && item.TryGetComponent<Image>(out var image))
            {
                Undo.RecordObject(image, "Keep brown spell cards");
                image.color = new Color(.18f, .095f, .045f, .9f);
            }
        }
    }

    // проверяем независимый выбор каждого разворота и отсутствие старых назначений справа.
    [MenuItem("Tools/Wizard War/Validate open book slots")]
    public static void Validate()
    {
        var menu = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<InteractiveShelfMenu>(true)).Single();
        if (menu.OpenBookSlots.Length != 3 || menu.OpenBookSlots.Select(book => book.Slot).Distinct().Count() != 3)
            throw new InvalidOperationException("Expected three unique slots.");
        Physics.SyncTransforms();
        foreach (OpenBookSlot slot in menu.OpenBookSlots)
        {
            Vector3 origin = new Vector3(-3.8f, .8f, -8.2f);
            if (menu.GetSlotBook(new Ray(origin, slot.HitArea.bounds.center - origin)) != slot)
                throw new InvalidOperationException("Open book is not independently clickable: " + slot.Slot);
            foreach (Canvas canvas in slot.GetComponentsInChildren<Canvas>(true))
                if (canvas.renderMode != RenderMode.WorldSpace || canvas.worldCamera != menu.MenuCamera)
                    throw new InvalidOperationException("Pages must use world space and the menu camera.");
            var data = new SerializedObject(slot);
            SerializedProperty symbols = data.FindProperty("elementSymbols");
            if (symbols.arraySize != 5)
                throw new InvalidOperationException("Each book needs five element symbols.");
            for (int index = 0; index < symbols.arraySize; index++)
                if (symbols.GetArrayElementAtIndex(index).objectReferenceValue == null)
                    throw new InvalidOperationException("Missing element symbol on slot " + slot.Slot);
        }
        foreach (Transform item in menu.Catalog.GetComponentsInChildren<Transform>(true))
            if ((item.name == "Slot 0" || item.name == "Slot 1" || item.name == "Slot 2") && item.gameObject.activeSelf)
                throw new InvalidOperationException("Old catalog slot is still visible.");
        File.WriteAllText("Logs/open-book-slots-validation.txt", "PASS: three unique clickable slots; six world-space pages; five symbols per slot; old catalog slots hidden.");
    }
}
