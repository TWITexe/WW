using System;
using TMPro;
using UnityEditor;
using UnityEngine;

// Updates the saved vitals prefab in place; runtime only updates values and its screen position.
public static class PlayerVitalsHUDBuilder
{
    const string BarPath = "Assets/Prefabs/UI/HealthBar.prefab";
    const string HudPath = "Assets/Prefabs/UI/MatchUI.prefab";
    static readonly Color Ink = new Color(.035f, .022f, .045f, .94f);
    static readonly Color Pink = new Color(1f, .34f, .62f);
    static readonly Color ShieldGray = new Color(.7f, .7f, .72f);
    static readonly Color StaminaBlue = new Color(.2f, .55f, 1f);

    [MenuItem("Tools/Wizard War/Update character vitals HUD")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing HUD assets.");
        using (var scope = new PrefabUtility.EditPrefabContentsScope(BarPath))
            StyleBar(scope.prefabContentsRoot);
        using (var scope = new PrefabUtility.EditPrefabContentsScope(HudPath))
        {
            var ui = scope.prefabContentsRoot.GetComponent<PlayerGameUI>();
            var data = new SerializedObject(ui);
            var canvas = (Canvas)data.FindProperty("canvas").objectReferenceValue;
            var root = (RectTransform)canvas.transform.Find("HealthBar");
            if (root == null) throw new InvalidOperationException("Existing HealthBar instance was not found.");
            Place(root, 0, 0, 112, 202);
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.anchoredPosition = new Vector2(-270, -150);
            var oldStamina = data.FindProperty("staminaFill").objectReferenceValue as UnityEngine.UI.Image;
            if (oldStamina != null && !oldStamina.transform.IsChildOf(root)) oldStamina.transform.parent.gameObject.SetActive(false);
            var oldLabel = data.FindProperty("staminaLabel").objectReferenceValue as UnityEngine.UI.Text;
            if (oldLabel != null) oldLabel.gameObject.SetActive(false);
            Bind(data, "vitalsRoot", root);
            Bind(data, "vitalsGroup", root.GetComponent<CanvasGroup>());
            Bind(data, "healthFill", root.Find("Health track/Fill").GetComponent<UnityEngine.UI.Image>());
            Bind(data, "shieldFill", root.Find("Shield track/Shield fill").GetComponent<UnityEngine.UI.Image>());
            Bind(data, "staminaFill", root.Find("Stamina track/Stamina inset/Stamina fill").GetComponent<UnityEngine.UI.Image>());
            Bind(data, "vitalsHealthText", root.Find("Health value").GetComponent<TMP_Text>());
            Bind(data, "vitalsMaxHealthText", root.Find("Health maximum").GetComponent<TMP_Text>());
            Bind(data, "vitalsShieldText", root.Find("Shield value").GetComponent<TMP_Text>());
            Bind(data, "vitalsStaminaText", root.Find("Stamina value").GetComponent<TMP_Text>());
            data.FindProperty("vitalsScreenOffset").vector2Value = new Vector2(-150, -215);
            data.FindProperty("vitalsAnchorHeight").floatValue = .65f;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        Debug.Log("CHARACTER_VITALS_HUD_SAVED");
    }

    static void StyleBar(GameObject go)
    {
        var root = go.GetComponent<RectTransform>();
        Place(root, -270, -150, 112, 202);
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        go.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
        var group = go.GetComponent<CanvasGroup>();
        if (group == null) group = go.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        root.Find("Value").gameObject.SetActive(false);
        var track = Box(root, "Health track", 0, 30, 34, 156, Ink);
        Box(track, "Empty", 3, 3, 28, 150, new Color(.24f, .075f, .15f, .96f));
        var fill = root.Find("Fill") ?? track.Find("Fill");
        fill.SetParent(track, false);
        Place((RectTransform)fill, 3, 3, 28, 150);
        Fill(fill.GetComponent<UnityEngine.UI.Image>(), Pink);
        fill.SetAsLastSibling();
        Box(track, "Highlight", 4, 3, 2, 150, new Color(1, .8f, .89f, .35f));
        for (int i = 1; i < 4; i++) Box(track, "Tick " + i, 26, 3 + i * 37.5f, 5, 1, new Color(.08f, .03f, .07f, .65f));
        var shield = Box(root, "Shield track", 39, 86, 10, 100, Ink);
        Box(shield, "Empty", 2, 2, 6, 96, new Color(.16f, .16f, .18f, .95f));
        Fill(Box(shield, "Shield fill", 2, 2, 6, 96, ShieldGray).GetComponent<UnityEngine.UI.Image>(), ShieldGray);
        shield.Find("Shield fill").GetComponent<UnityEngine.UI.Image>().fillAmount = 0;
        Box(root, "Health value backing", 16, 30, 80, 50, Ink);
        Label(root, "Health value", "100", 21, 49, 74, 31, 28, Color.white, FontStyles.Bold);
        Label(root, "Health maximum", "/ 100", 37, 33, 62, 17, 12, new Color(1, .77f, .86f));
        Label(root, "Shield value", "0", 53, 158, 54, 23, 16, ShieldGray, FontStyles.Bold).gameObject.SetActive(false);
        var stamina = Box(root, "Stamina track", 0, 10, 70, 12, Ink);
        var inset = Box(stamina, "Stamina inset", 3, 3, 64, 6, new Color(.055f, .11f, .22f, .95f));
        var staminaFill = Box(inset, "Stamina fill", 0, 0, 0, 0, StaminaBlue);
        staminaFill.anchorMin = Vector2.zero; staminaFill.anchorMax = Vector2.one;
        staminaFill.offsetMin = staminaFill.offsetMax = Vector2.zero;
        Label(root, "Stamina value", "100", 76, 6, 35, 20, 12, StaminaBlue);
        foreach (var graphic in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
    }

    static void Bind(SerializedObject data, string name, UnityEngine.Object value) => data.FindProperty(name).objectReferenceValue = value;

    static void Fill(UnityEngine.UI.Image image, Color color)
    {
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Prefabs/UI/CooldownMask.png");
        image.type = UnityEngine.UI.Image.Type.Filled;
        image.fillMethod = UnityEngine.UI.Image.FillMethod.Vertical;
        image.fillOrigin = 0; image.fillAmount = 1; image.color = color;
    }

    static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var child = parent.Find(name);
        var go = child != null ? child.gameObject : new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); Place(rect, x, y, w, h);
        var image = go.GetComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
        return rect;
    }

    static TMP_Text Label(Transform parent, string name, string value, float x, float y, float w, float h, float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var child = parent.Find(name);
        var go = child != null ? child.gameObject : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TMP_Text>(); Place(text.rectTransform, x, y, w, h);
        text.font = TMP_Settings.defaultFontAsset; text.fontSize = size; text.fontStyle = style;
        text.text = value; text.color = color; text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.raycastTarget = false;
        return text;
    }

    static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h);
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
}
