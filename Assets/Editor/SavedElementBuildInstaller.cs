using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Дополняет существующий каталог, не пересоздавая сцену и её ручную вёрстку.
public static class SavedElementBuildInstaller
{
    private static readonly Color Ink = new Color(.94f, .90f, .79f);
    private static readonly Color Surface = new Color(.14f, .12f, .09f, 1);
    private static TMP_FontAsset font;

    [MenuItem("Tools/Wizard War/Install saved element builds")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Menu.unity") throw new InvalidOperationException("Open the Menu scene first.");
        var catalog = UnityEngine.Object.FindFirstObjectByType<ShelfSpellCatalogUI>(FindObjectsInactive.Include);
        if (catalog == null) throw new InvalidOperationException("Shelf catalog missing.");
        var installed = catalog.GetComponentInChildren<SavedElementBuildUI>(true);
        if (installed != null)
        {
            FinishLayout(installed);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Validate();
            return;
        }
        var background = catalog.transform.Find("Catalog background") as RectTransform;
        var hint = background != null ? background.Find("Controls") as RectTransform : null;
        var viewport = background != null ? background.Find("Scroll viewport") as RectTransform : null;
        if (hint == null || viewport == null) throw new InvalidOperationException("Catalog layout does not match.");
        Directory.CreateDirectory("Logs/SavedElementBuilds");
        EditorSceneManager.SaveScene(scene, "Logs/SavedElementBuilds/Menu-before-builds.unity", true);
        font = CreateFont();

        var footer = new GameObject("Saved element builds", typeof(RectTransform), typeof(CanvasGroup), typeof(SavedElementBuildUI));
        Undo.RegisterCreatedObjectUndo(footer, "Add saved element builds");
        footer.transform.SetParent(background, false);
        Place(footer.GetComponent<RectTransform>(), 20, -610, 680, 106);
        var resources = new TMP_DefaultControls.Resources();
        var inputObject = TMP_DefaultControls.CreateInputField(resources);
        inputObject.name = "Build name";
        inputObject.transform.SetParent(footer.transform, false);
        Place(inputObject.GetComponent<RectTransform>(), 0, 0, 425, 38);
        var input = inputObject.GetComponent<TMP_InputField>();
        input.characterLimit = SavedElementBuilds.NameLimit;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;
        input.fontAsset = font;
        ((TMP_Text)input.placeholder).text = "Название нового билда…";
        inputObject.GetComponent<UnityEngine.UI.Image>().color = Surface;
        var save = Button(footer.transform, "Save build", "Сохранить билд", 435, 0, 245, out TMP_Text saveLabel);

        var pickerObject = TMP_DefaultControls.CreateDropdown(resources);
        pickerObject.name = "Saved builds";
        pickerObject.transform.SetParent(footer.transform, false);
        Place(pickerObject.GetComponent<RectTransform>(), 0, -44, 550, 38);
        var picker = pickerObject.GetComponent<TMP_Dropdown>();
        picker.ClearOptions();
        picker.AddOptions(new System.Collections.Generic.List<string> { "Текущий набор" });
        pickerObject.GetComponent<UnityEngine.UI.Image>().color = Surface;
        picker.template.GetComponent<UnityEngine.UI.Image>().color = Surface;
        // Раскрываем список вверх над спеллами, внутри мирового Canvas.
        picker.template.anchorMin = new Vector2(0, 1);
        picker.template.anchorMax = Vector2.one;
        picker.template.pivot = new Vector2(.5f, 0);
        picker.template.anchoredPosition = new Vector2(0, 4);
        picker.template.sizeDelta = new Vector2(0, 230);
        var item = picker.itemText.transform.parent.GetComponent<RectTransform>();
        item.sizeDelta = new Vector2(0, 38);
        ((RectTransform)item.parent).sizeDelta = new Vector2(0, 46);
        item.Find("Item Background").GetComponent<UnityEngine.UI.Image>().color = new Color(.28f, .23f, .15f);
        var arrow = pickerObject.transform.Find("Arrow");
        arrow.GetComponent<UnityEngine.UI.Image>().enabled = false;
        var arrowLabel = Label(arrow, "Arrow label", "v", 0, 0, 20, 20, 18);
        var delete = Button(footer.transform, "Delete build", "Удалить", 560, -44, 120, out TMP_Text deleteLabel);
        TMP_Text status = Label(footer.transform, "Build status", "", 0, -84, 680, 22, 18);
        foreach (TMP_Text text in footer.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = font;
            text.fontSize = text == status ? 18 : 22;
            text.color = Ink;
            text.richText = false;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }
        ((TMP_Text)input.placeholder).color = new Color(.68f, .65f, .57f);
        arrowLabel.fontSize = 18;
        arrowLabel.alignment = TextAlignmentOptions.Center;
        arrowLabel.overflowMode = TextOverflowModes.Overflow;
        foreach (Transform child in footer.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = catalog.gameObject.layer;
        var ui = footer.GetComponent<SavedElementBuildUI>();
        ui.Configure(input, picker, save, delete, saveLabel, deleteLabel, status, footer.GetComponent<CanvasGroup>());
        FinishLayout(ui);
        Undo.RecordObject(catalog, "Connect saved builds");
        catalog.ConfigureBuilds(ui);
        Undo.RecordObject(hint, "Move controls hint below builds");
        Place(hint, 20, -718, 680, 36);
        var hintText = hint.GetComponent<UnityEngine.UI.Text>();
        Undo.RecordObject(hintText, "Fit controls hint");
        hintText.fontSize = 16;
        // Для прежней вёрстки освобождаем только нижнюю часть списка.
        if (-viewport.anchoredPosition.y + viewport.rect.height > 602)
        {
            Undo.RecordObject(viewport, "Reserve build footer");
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 602 + viewport.anchoredPosition.y);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Validate();
    }

    private static TMP_FontAsset CreateFont()
    {
        const string path = "Assets/UI/SavedBuildNamesFont.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
        var result = TMP_FontAsset.CreateFontAsset(source);
        result.name = "Saved Build Names";
        result.TryAddCharacters("АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюяABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:!?—…▾");
        AssetDatabase.CreateAsset(result, path);
        AssetDatabase.AddObjectToAsset(result.material, result);
        foreach (Texture2D atlas in result.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, result);
        return result;
    }

    private static void FinishLayout(SavedElementBuildUI ui)
    {
        var arrow = ui.transform.Find("Saved builds/Arrow/Arrow label").GetComponent<TMP_Text>();
        arrow.text = "v";
        arrow.fontSize = 18;
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.overflowMode = TextOverflowModes.Overflow;
        var picker = ui.GetComponentInChildren<TMP_Dropdown>(true);
        picker.ClearOptions();
        picker.AddOptions(new System.Collections.Generic.List<string> { "Нет сохранённых билдов" });
        picker.interactable = false;
        ui.transform.Find("Delete build").GetComponent<UnityEngine.UI.Button>().interactable = false;
        ui.transform.Find("Build status").GetComponent<TMP_Text>().text = "Назовите текущий набор и нажмите «Сохранить билд».";
    }

    private static UnityEngine.UI.Button Button(Transform parent, string name, string caption,
        float x, float y, float width, out TMP_Text label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), x, y, width, 38);
        go.GetComponent<UnityEngine.UI.Image>().color = new Color(.30f, .23f, .12f);
        var button = go.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = go.GetComponent<UnityEngine.UI.Image>();
        label = Label(go.transform, "Label", caption, 4, 0, width - 8, 38, 22);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float width, float height, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), x, y, width, height);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = size; label.color = Ink;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        return label;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    [MenuItem("Tools/Wizard War/Validate saved element builds")]
    public static void Validate()
    {
        SavedElementBuildRegression.Run();
        var catalog = UnityEngine.Object.FindFirstObjectByType<ShelfSpellCatalogUI>(FindObjectsInactive.Include);
        var ui = catalog.GetComponentInChildren<SavedElementBuildUI>(true);
        if (ui == null || catalog.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            throw new InvalidOperationException("Build UI or input raycaster missing.");
        var serialized = new SerializedObject(ui);
        foreach (string field in new[] { "buildName", "buildPicker", "saveButton", "deleteButton", "saveLabel", "deleteLabel", "status", "inputGroup" })
            if (serialized.FindProperty(field).objectReferenceValue == null) throw new InvalidOperationException("Missing " + field);
        if (UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length != 1)
            throw new InvalidOperationException("Expected one EventSystem.");
        SavedElementBuildRegression.CheckUI(ui);
        Directory.CreateDirectory("Logs/SavedElementBuilds");
        File.WriteAllText("Logs/SavedElementBuilds/validation.txt", "PASS: persistence, save/select/replace/delete UI callbacks, ordered Q/E/R application, scene references, raycaster and EventSystem.");
        Debug.Log("SAVED_ELEMENT_BUILDS_VALIDATED");
    }

    public static void Preview()
    {
        var menu = UnityEngine.Object.FindFirstObjectByType<InteractiveShelfMenu>(FindObjectsInactive.Include);
        Camera camera = menu.MenuCamera;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        bool visible = menu.Catalog.gameObject.activeSelf;
        var target = new RenderTexture(1920, 1080, 24);
        var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            var serialized = new SerializedObject(menu);
            camera.transform.SetPositionAndRotation(serialized.FindProperty("shelfPosition").vector3Value,
                Quaternion.Euler(serialized.FindProperty("shelfAngles").vector3Value));
            camera.targetTexture = target;
            menu.Catalog.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            texture.Apply();
            Directory.CreateDirectory("Logs/SavedElementBuilds");
            File.WriteAllBytes("Logs/SavedElementBuilds/shelves.png", texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            menu.Catalog.gameObject.SetActive(visible);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
