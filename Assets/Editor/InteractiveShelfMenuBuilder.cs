using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// устанавливает пространственное меню в открытую сцену, сохраняя расстановку настоящих книг.
public static class InteractiveShelfMenuBuilder
{
    private static readonly Color Ink = new Color(.9f, .88f, .8f);
    private static readonly Color Gold = new Color(.91f, .72f, .38f);

    // добавляем меню один раз; повторный запуск не перезаписывает ручную настройку объектов.
    [MenuItem("Tools/Wizard War/Install interactive shelves")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode before installing the shelves.");
        if (Object.FindFirstObjectByType<InteractiveShelfMenu>(FindObjectsInactive.Include) != null)
            throw new InvalidOperationException("Interactive shelves already exist; edit their saved settings in the Inspector.");

        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Menu camera is missing.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab")
            .GetComponentInChildren<SpellManager>(true);
        var oldBook = Object.FindFirstObjectByType<ElementLoadoutUI>(FindObjectsInactive.Include);
        string[] names = { "Fire book", "Air book", "Ice book", "Earth book (1)", "Water book" };
        Transform[] models = names.Select(name => Find(scene, name)).ToArray();
        Transform shelf = Find(scene, "Dangeon/Shelf (1)");
        Transform board = Find(scene, "Dangeon/Shelf (2)");

        // сохраняем текущую авторскую сцену до установки, включая правки, ещё не записанные на диск.
        Directory.CreateDirectory("Logs/InteractiveShelfMenu");
        EditorSceneManager.SaveScene(scene);
        File.Copy(scene.path, "Logs/InteractiveShelfMenu/Menu-before-install.unity", true);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Install interactive shelves");

        var root = new GameObject("Interactive Shelf Menu");
        Undo.RegisterCreatedObjectUndo(root, "Create shelf menu");
        var menu = root.AddComponent<InteractiveShelfMenu>();
        var books = new ElementBook[models.Length];
        for (int index = 0; index < models.Length; index++)
        {
            Transform model = models[index];
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = BoundsOf(renderers);
            var hitObject = new GameObject("Book hit area - " + (MagicElement)index);
            hitObject.transform.SetParent(root.transform, false);
            hitObject.transform.position = bounds.center;
            var collider = hitObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = bounds.size + new Vector3(.025f, .025f, .025f);
            var book = hitObject.AddComponent<ElementBook>();
            book.Configure((MagicElement)index, collider, renderers);
            books[index] = book;
        }

        Bounds shelfBounds = BoundsOf(shelf.GetComponentsInChildren<Renderer>(true));
        shelfBounds.Encapsulate(BoundsOf(board.GetComponentsInChildren<Renderer>(true)));
        var shelfArea = new GameObject("Whole bookshelf hit area");
        shelfArea.transform.SetParent(root.transform, false);
        shelfArea.transform.position = shelfBounds.center;
        var shelfCollider = shelfArea.AddComponent<BoxCollider>();
        shelfCollider.isTrigger = true;
        shelfCollider.size = shelfBounds.size + Vector3.one * .04f;

        ShelfSpellCatalogUI catalog = BuildCatalog(root.transform, camera, source.Spells);
        GameObject mainCanvas = scene.GetRootGameObjects().FirstOrDefault(item => item.name == "CanvasMain");
        menu.Configure(camera, shelfCollider, books, catalog, mainCanvas != null ? new[] { mainCanvas } : Array.Empty<GameObject>());

        // прежнюю книгу сохраняем в сцене выключенной: ссылки и ручную вёрстку можно восстановить.
        if (oldBook != null)
        {
            Undo.RecordObject(oldBook.gameObject, "Disable overlay spellbook");
            oldBook.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldBook.gameObject);
        }
        Undo.RecordObject(camera.transform, "Set menu overview camera");
        camera.transform.SetPositionAndRotation(new Vector3(-5.54f, 1.66f, -6.94f), Quaternion.Euler(20.5f, 137.4f, 0));
        catalog.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Validate();
        CapturePreviews(menu);
        File.WriteAllText("Logs/menu-shelf-installed.txt", "Installed and saved interactive shelves in Menu.");
    }

    // создаём каталог в мировых координатах на пустой правой поверхности полок.
    private static ShelfSpellCatalogUI BuildCatalog(Transform parent, Camera camera, IReadOnlyList<Spell> spells)
    {
        var root = new GameObject("World Spell Catalog", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ShelfSpellCatalogUI));
        root.transform.SetParent(parent, false);
        root.layer = 5;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(720, 760);
        rect.position = new Vector3(-2.4f, .72f, -8.9f);
        rect.rotation = Quaternion.Euler(0, 90, 0);
        rect.localScale = Vector3.one * .00155f;
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.sortingOrder = 10;
        root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2;

        RectTransform background = Panel(root.transform, "Catalog background", Vector2.zero,
            new Vector2(720, 760), new Color(.055f, .06f, .055f, .94f));
        Text title = Label(background, "Title", "КНИГА ЗАКЛИНАНИЙ", new Vector2(28, -22), new Vector2(664, 38), 26, Gold);
        Label(background, "Loadout caption", "ТВОЙ НАБОР СТИХИЙ", new Vector2(28, -68), new Vector2(664, 28), 20, Ink);
        var labels = new Text[3];
        var buttons = new Button[3];
        for (int slot = 0; slot < 3; slot++)
        {
            RectTransform slotRoot = Panel(background, "Slot " + slot, new Vector2(24 + slot * 225, -105),
                new Vector2(218, 46), new Color(.2f, .21f, .19f, .85f));
            buttons[slot] = slotRoot.gameObject.AddComponent<Button>();
            buttons[slot].navigation = new Navigation { mode = Navigation.Mode.None };
            labels[slot] = Label(slotRoot, "Slot label", "", new Vector2(8, -5), new Vector2(202, 36), 24, Ink);
            labels[slot].alignment = TextAnchor.MiddleCenter;
        }

        RectTransform viewport = Panel(background, "Scroll viewport", new Vector2(20, -171),
            new Vector2(680, 493), new Color(0, 0, 0, .05f));
        viewport.gameObject.AddComponent<RectMask2D>();
        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        var content = new GameObject("Spell cards", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(.5f, 1);
        contentRect.sizeDelta = Vector2.zero;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 38;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var cards = new List<ShelfSpellCatalogUI.SpellCard>();
        foreach (Spell spell in spells)
        {
            if (spell == null) continue;
            RectTransform card = Panel(content.transform, spell.name, Vector2.zero, new Vector2(672, 144), new Color(.18f, .095f, .045f, .9f));
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 144;
            var iconRoot = new GameObject("Spell icon", typeof(RectTransform), typeof(SpellIconGraphic));
            iconRoot.transform.SetParent(card, false);
            Place(iconRoot.GetComponent<RectTransform>(), new Vector2(12, -16), new Vector2(56, 56));
            var icon = iconRoot.GetComponent<SpellIconGraphic>();
            icon.spell = spell;
            icon.color = SpellIconGraphic.Tint(spell);
            icon.raycastTarget = false;
            Label(card, "Spell name", spell.Name, new Vector2(82, -8), new Vector2(572, 36), 27, Ink);
            Text recipe = Label(card, "Recipe", "", new Vector2(82, -45), new Vector2(572, 29), 23, new Color(.53f, .86f, .78f));
            Label(card, "Description", spell.Description, new Vector2(82, -80), new Vector2(568, 60), 21, new Color(.77f, .8f, .8f));
            cards.Add(new ShelfSpellCatalogUI.SpellCard { spell = spell, root = card.gameObject, recipe = recipe });
        }
        Text hint = Label(background, "Controls", "", new Vector2(28, -689), new Vector2(664, 60), 21, Ink);
        var catalog = root.GetComponent<ShelfSpellCatalogUI>();
        catalog.Configure(labels, buttons, scroll, cards);
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
        return catalog;
    }

    // строим панель от верхнего левого угла родителя.
    private static RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        Place(rect, position, size);
        item.GetComponent<Image>().color = color;
        return rect;
    }

    // задаём верхний левый якорь, чтобы координаты интерфейса читались как отступы от края.
    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // создаём русскую подпись без перехвата событий мыши поверх прокручиваемой области.
    private static Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(Text));
        item.transform.SetParent(parent, false);
        Text text = item.GetComponent<Text>();
        Place(text.rectTransform, position, size);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    // ищем объект по полному пути либо по уникальному имени, прерывая установку при неоднозначности.
    private static Transform Find(Scene scene, string path)
    {
        if (path.Contains("/"))
        {
            string[] parts = path.Split(new[] { '/' }, 2);
            GameObject root = scene.GetRootGameObjects().Single(item => item.name == parts[0]);
            return root.transform.Find(parts[1]) ?? throw new InvalidOperationException("Missing object: " + path);
        }
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Single(item => item.name == path);
    }

    // объединяем фактические размеры модели для области нажатия, не изменяя её геометрию.
    private static Bounds BoundsOf(Renderer[] renderers)
    {
        if (renderers.Length == 0) throw new InvalidOperationException("Book or shelf has no renderers.");
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    // проверяем ссылки и попадание лучей в каждую книгу без запуска игры и изменения сохранённых стихий.
    [MenuItem("Tools/Wizard War/Validate interactive shelves")]
    public static void Validate()
    {
        var menu = Object.FindFirstObjectByType<InteractiveShelfMenu>(FindObjectsInactive.Include);
        if (menu == null || menu.Books.Length != 5 || menu.Catalog == null || menu.MenuCamera == null)
            throw new InvalidOperationException("Shelf references are incomplete.");
        if (menu.Books.Select(book => book.Element).Distinct().Count() != 5)
            throw new InvalidOperationException("Book elements must be unique.");
        Canvas canvas = menu.Catalog.GetComponent<Canvas>();
        if (canvas.renderMode != RenderMode.WorldSpace || canvas.worldCamera != menu.MenuCamera)
            throw new InvalidOperationException("Catalog must use the menu camera in world space.");
        Physics.SyncTransforms();
        foreach (ElementBook book in menu.Books)
        {
            Vector3 origin = new Vector3(-3.8f, .8f, -8.2f);
            Ray ray = new Ray(origin, book.HitArea.bounds.center - origin);
            if (menu.FindBook(ray) != book) throw new InvalidOperationException("Book is not independently clickable: " + book.Element);
        }
        // проверяем, что декоративный overlay не перекрывает всю область полок в общем ракурсе.
        var pointer = new PointerEventData(EventSystem.current);
        pointer.position = menu.MenuCamera.WorldToScreenPoint(menu.Books[0].HitArea.bounds.center);
        var hits = new List<RaycastResult>();
        if (EventSystem.current != null) EventSystem.current.RaycastAll(pointer, hits);
        File.WriteAllLines("Logs/InteractiveShelfMenu/pointer-blockers.txt", hits.Select(hit => hit.gameObject.name));
        File.WriteAllText("Logs/menu-shelf-validation.txt", "PASS: five unique clickable books; world-space catalog; camera and scene references.");
        CapturePreviews(menu);
    }

    // сохраняем два редакторских ракурса и возвращаем сцене исходное состояние после рендера.
    private static void CapturePreviews(InteractiveShelfMenu menu)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        Camera camera = menu.MenuCamera;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        bool visible = menu.Catalog.gameObject.activeSelf;
        var target = new RenderTexture(1280, 720, 24);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            foreach (ElementBook book in menu.Books)
                book.SetAppearance(ElementLoadout.Default.Contains(book.Element), false);
            for (int index = 0; index < 2; index++)
            {
                bool close = index == 1;
                camera.transform.SetPositionAndRotation(close ? new Vector3(-3.8f, .8f, -8.2f) : position,
                    close ? Quaternion.Euler(9.16f, 94.78f, 0) : rotation);
                menu.Catalog.gameObject.SetActive(close);
                foreach (OpenBookSlot slot in menu.OpenBookSlots)
                {
                    slot.SetVisible(close);
                    slot.Refresh(ElementLoadout.Default, 0);
                }
                menu.Catalog.Refresh(ElementLoadout.Default, 0);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                File.WriteAllBytes("Logs/InteractiveShelfMenu/" + (close ? "shelves" : "overview") + ".png", texture.EncodeToPNG());
            }
        }
        finally
        {
            foreach (ElementBook book in menu.Books) book.RestoreAppearance();
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            menu.Catalog.gameObject.SetActive(visible);
            foreach (OpenBookSlot slot in menu.OpenBookSlots) slot.SetVisible(visible);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(target);
        }
    }
}
