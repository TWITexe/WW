using System;
using System.Reflection;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;

// Отдельный временный ключ не затрагивает пользовательские билды или стихии.
public static class SavedElementBuildRegression
{
    public static void Run()
    {
        string key = "Tests.SavedBuilds." + Guid.NewGuid().ToString("N");
        try
        {
            var store = new SavedElementBuilds(key);
            var first = new ElementLoadout { q = MagicElement.Water, e = MagicElement.Earth, r = MagicElement.Fire };
            var second = new ElementLoadout { q = MagicElement.Fire, e = MagicElement.Water, r = MagicElement.Earth };
            Check(store.Builds.Count == 0, "Empty storage");
            Check(!store.Save("  ", first), "Blank name rejected");
            Check(!store.Save(new string('а', 33), first), "Long name rejected");
            Check(!store.Save("Line\nBreak", first), "Control characters rejected");
            Check(!store.Save("Bad", default), "Duplicate elements rejected");
            Check(!store.Save("Bad", new ElementLoadout { q = (MagicElement)99, e = MagicElement.Air, r = MagicElement.Ice }), "Invalid enum rejected");
            Check(store.Save("  Мой билд  ", first), "Cyrillic name saved");
            first.q = MagicElement.Ice;
            store = new SavedElementBuilds(key);
            Check(store.Builds.Count == 1 && store.Builds[0].Name == "Мой билд", "Trimmed name persists");
            Check(store.Builds[0].Loadout.q == MagicElement.Water && store.Builds[0].Loadout.e == MagicElement.Earth && store.Builds[0].Loadout.r == MagicElement.Fire, "Independent ordered snapshot persists");
            Check(!store.Save("МОЙ БИЛД", second), "Duplicate name needs confirmation");
            Check(store.Save("МОЙ БИЛД", second, true), "Confirmed replacement");
            Check(store.Save("Второй", ElementLoadout.Default), "Multiple builds");
            store = new SavedElementBuilds(key);
            Check(store.Builds.Count == 2 && store.Builds[0].Loadout.e == MagicElement.Water, "Replacement persists");
            Check(store.Delete("мой билд") && !store.Delete("missing"), "Delete by name");
            Check(new SavedElementBuilds(key).Builds.Count == 1, "Deletion persists");
            PlayerPrefs.SetString(key, "{\"builds\":[null,{\"name\":\"Bad\",\"loadout\":{\"q\":0,\"e\":0,\"r\":0}},{\"name\":\"Good\",\"loadout\":{\"q\":4,\"e\":3,\"r\":0}}]}");
            Check(new SavedElementBuilds(key).Builds.Count == 1, "Invalid records skipped independently");
            PlayerPrefs.SetString(key, "{broken json");
            Check(new SavedElementBuilds(key).Builds.Count == 0, "Malformed storage tolerated");
            PlayerPrefs.DeleteKey(key);
            store = new SavedElementBuilds(key);
            for (int i = 0; i < SavedElementBuilds.Capacity; i++) Check(store.Save("Build " + i, second), "Capacity fill");
            Check(!store.Save("Overflow", second) && store.Save("Build 0", ElementLoadout.Default, true), "Capacity allows replacement only");
            Debug.Log("SAVED_ELEMENT_BUILDS_REGRESSION_PASSED");
        }
        finally { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Saved builds: " + message);
    }

    public static void CheckUI(SavedElementBuildUI source)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run the regression outside Play Mode.");
        var originalSettings = LocalPlayerSettings.Instance;
        string key = "Tests.BuildUI." + Guid.NewGuid().ToString("N");
        string[] slots = { "Elements.Q", "Elements.E", "Elements.R" };
        var hadKeys = Array.ConvertAll(slots, PlayerPrefs.HasKey);
        var oldValues = Array.ConvertAll(slots, slot => PlayerPrefs.GetInt(slot));
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Build UI regression");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var settings = root.AddComponent<LocalPlayerSettings>();
            typeof(LocalPlayerSettings).GetProperty("Instance").SetValue(null, settings);
            typeof(LocalPlayerSettings).GetProperty("Loadout").SetValue(settings, ElementLoadout.Default);
            typeof(LocalPlayerSettings).GetProperty("SavedBuilds").SetValue(settings, new SavedElementBuilds(key));
            var menu = root.AddComponent<InteractiveShelfMenu>();
            Set(menu, "books", Array.Empty<ElementBook>());
            var state = typeof(InteractiveShelfMenu).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
            state.SetValue(menu, Enum.Parse(state.FieldType, "Shelf"));

            var catalogObject = new GameObject("Catalog", typeof(RectTransform), typeof(ShelfSpellCatalogUI));
            catalogObject.transform.SetParent(root.transform, false);
            var catalog = catalogObject.GetComponent<ShelfSpellCatalogUI>();
            var title = new GameObject("Title", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            title.transform.SetParent(catalogObject.transform, false);
            var hint = new GameObject("Hint", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            hint.transform.SetParent(catalogObject.transform, false);
            var scroll = catalogObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.content = title.GetComponent<RectTransform>();
            scroll.viewport = catalogObject.GetComponent<RectTransform>();
            var waterCard = new GameObject("Water card", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            waterCard.transform.SetParent(catalog.transform, false);
            var waterRecipe = waterCard.GetComponent<UnityEngine.UI.Text>();
            catalog.Configure(title.GetComponent<UnityEngine.UI.Text>(), hint.GetComponent<UnityEngine.UI.Text>(),
                Array.Empty<UnityEngine.UI.Text>(), Array.Empty<UnityEngine.UI.Button>(), scroll,
                new System.Collections.Generic.List<ShelfSpellCatalogUI.SpellCard> { new ShelfSpellCatalogUI.SpellCard {
                    spell = UnityEditor.AssetDatabase.LoadAssetAtPath<Spell>("Assets/Scripts/Spells/Elemental/WaterBolt.asset"),
                    root = waterCard, recipe = waterRecipe } });
            Set(menu, "catalog", catalog);
            var ui = UnityEngine.Object.Instantiate(source, catalog.transform);
            catalog.ConfigureBuilds(ui);
            ui.Bind(menu);
            ui.SetInteraction(true);
            var input = Get<TMP_InputField>(ui, "buildName");
            var picker = Get<TMP_Dropdown>(ui, "buildPicker");
            var save = Get<UnityEngine.UI.Button>(ui, "saveButton");
            var delete = Get<UnityEngine.UI.Button>(ui, "deleteButton");
            input.text = "Первый";
            save.onClick.Invoke();
            Check(settings.SavedBuilds.Builds.Count == 1 && picker.value == 1, "Save button creates and selects build");
            var alternate = new ElementLoadout { q = MagicElement.Water, e = MagicElement.Earth, r = MagicElement.Fire };
            Check(menu.ApplyBuild(alternate) && picker.value == 0, "Manual change deselects saved build");
            input.text = "Второй";
            save.onClick.Invoke();
            picker.value = 1;
            Check(settings.Loadout.q == MagicElement.Fire && settings.Loadout.e == MagicElement.Air && settings.Loadout.r == MagicElement.Ice, "Picker applies all slots atomically");
            Check(!waterCard.activeSelf, "Unavailable spell hidden after switching");
            picker.value = 2;
            Check(settings.Loadout.q == MagicElement.Water && PlayerPrefs.GetInt("Elements.Q") == 4, "Picker persists active slots");
            Check(waterCard.activeSelf && waterRecipe.text.StartsWith("Q → Q → Q"), "Spell availability and recipe keys refresh");
            Check(!settings.ApplyLoadout(default) && settings.Loadout.q == MagicElement.Water, "Invalid apply leaves current build intact");
            input.text = "Первый";
            save.onClick.Invoke();
            Check(settings.SavedBuilds.Builds[0].Loadout.q == MagicElement.Fire, "First overwrite click preserves build");
            save.onClick.Invoke();
            Check(settings.SavedBuilds.Builds[0].Loadout.q == MagicElement.Water, "Second overwrite click replaces build");
            delete.onClick.Invoke();
            Check(settings.SavedBuilds.Builds.Count == 2, "First delete click preserves build");
            delete.onClick.Invoke();
            Check(settings.SavedBuilds.Builds.Count == 1 && settings.Loadout.q == MagicElement.Water, "Delete preserves current slots");
            ui.SetInteraction(false);
            Check(!Get<CanvasGroup>(ui, "inputGroup").interactable, "Input disabled outside shelves");
            Debug.Log("SAVED_ELEMENT_BUILD_UI_REGRESSION_PASSED");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            typeof(LocalPlayerSettings).GetProperty("Instance").SetValue(null, originalSettings);
            EditorSceneManager.ClosePreviewScene(scene);
            for (int i = 0; i < slots.Length; i++)
                if (hadKeys[i]) PlayerPrefs.SetInt(slots[i], oldValues[i]); else PlayerPrefs.DeleteKey(slots[i]);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }

    private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
}
