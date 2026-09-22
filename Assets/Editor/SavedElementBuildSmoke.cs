using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

// Короткая Play Mode проверка настоящего dropdown и фокуса; настройки восстанавливаются при выходе.
[InitializeOnLoad]
public static class SavedElementBuildSmoke
{
    private const string Flag = "SavedBuildSmoke.Active";
    private const string TestKey = "Tests.SavedBuildSmoke";
    private static int stage;
    private static double started, next;
    private static SavedElementBuildUI ui;
    private static InteractiveShelfMenu menu;
    private static TMP_Dropdown picker;
    private static TMP_InputField input;

    static SavedElementBuildSmoke()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Flag, false)) return;
            foreach (string slot in new[] { "Q", "E", "R" })
            {
                string key = "Elements." + slot;
                if (SessionState.GetBool(Flag + slot, false)) PlayerPrefs.SetInt(key, SessionState.GetInt(Flag + slot, 0));
                else PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.DeleteKey(TestKey);
            PlayerPrefs.Save();
            SessionState.SetBool(Flag, false);
            File.AppendAllText("Logs/SavedElementBuilds/play-mode.txt", "\nRestored player preferences and returned to Edit Mode.");
        };
    }

    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing.");
        stage = 0;
        started = next = 0;
        foreach (string slot in new[] { "Q", "E", "R" })
        {
            string key = "Elements." + slot;
            SessionState.SetBool(Flag + slot, PlayerPrefs.HasKey(key));
            SessionState.SetInt(Flag + slot, PlayerPrefs.GetInt(key));
        }
        SessionState.SetBool(Flag, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (started == 0) started = now;
            if (now - started > 30) throw new Exception("Play Mode timeout.");
            if (now < next) return;
            if (stage == 0)
            {
                menu = UnityEngine.Object.FindFirstObjectByType<InteractiveShelfMenu>();
                if (menu == null || LocalPlayerSettings.Instance == null) return;
                menu.BeginFlight(true);
                stage = 1;
                next = now + 1.5;
            }
            else if (stage == 1)
            {
                if (!menu.IsFocused) return;
                var store = new SavedElementBuilds(TestKey);
                store.Save("Огонь, воздух и лёд", ElementLoadout.Default, true);
                store.Save("Вода, земля и огонь", new ElementLoadout { q = MagicElement.Water, e = MagicElement.Earth, r = MagicElement.Fire }, true);
                typeof(LocalPlayerSettings).GetProperty("SavedBuilds").SetValue(LocalPlayerSettings.Instance, store);
                ui = menu.Catalog.GetComponentInChildren<SavedElementBuildUI>();
                ui.Bind(menu);
                picker = ui.GetComponentInChildren<TMP_Dropdown>();
                input = ui.GetComponentInChildren<TMP_InputField>();
                picker.Show();
                stage = 2;
                next = now + .4;
            }
            else if (stage == 2)
            {
                if (!picker.IsExpanded || !ui.HandlesInput) throw new Exception("Dropdown did not open or capture keys.");
                var list = (GameObject)typeof(TMP_Dropdown).GetField("m_Dropdown", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(picker);
                var toggles = list.GetComponentsInChildren<UnityEngine.UI.Toggle>();
                if (toggles.Length != 3) throw new Exception("Expected current set and two saved builds.");
                SavedElementBuildInstaller.Preview();
                File.Copy("Logs/SavedElementBuilds/shelves.png", "Logs/SavedElementBuilds/dropdown.png", true);
                toggles[2].isOn = true;
                if (LocalPlayerSettings.Instance.Loadout.q != MagicElement.Water || LocalPlayerSettings.Instance.Loadout.e != MagicElement.Earth)
                    throw new Exception("Dropdown click failed to apply build.");
                stage = 3;
                next = EditorApplication.timeSinceStartup + .3;
            }
            else if (stage == 3)
            {
                if (picker.IsExpanded)
                {
                    if (now > next + 2) throw new Exception("Dropdown did not close after selection.");
                    return;
                }
                input.Select();
                input.ActivateInputField();
                stage = 4;
                next = now + .2;
            }
            else if (stage == 4)
            {
                if (!input.isFocused || !ui.HandlesInput) throw new Exception("Name input failed to capture keyboard.");
                ui.SetInteraction(false);
                if (input.isFocused) throw new Exception("Name input retained focus after leaving shelves.");
                File.WriteAllText("Logs/SavedElementBuilds/play-mode.txt", "PASS: dropdown opens, lists saved builds, applies selection, closes, captures input and releases focus.");
                stage = 5;
                EditorApplication.isPlaying = false;
            }
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory("Logs/SavedElementBuilds");
            File.WriteAllText("Logs/SavedElementBuilds/play-mode.txt", "FAIL: " + exception);
            stage = 5;
            EditorApplication.isPlaying = false;
        }
    }
}
