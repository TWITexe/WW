using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// добавляет читаемую галочку возле каждого образца цвета на планшете.
public static class ColorSelectionMarkBuilder
{
    // сохраняем отметки в сцене, не создавая графику заново при каждом открытии настройки.
    [MenuItem("Tools/Wizard War/Add color selection marks")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        var desk = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DeskCustomizationUI>(true)).Single();
        ColorButton[] buttons = desk.GetComponentsInChildren<ColorButton>(true);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add selected color checkmarks");
        foreach (ColorButton button in buttons)
        {
            var data = new SerializedObject(button);
            if (data.FindProperty("selectedMark").objectReferenceValue == null)
            {
                var mark = new GameObject("Selected color checkmark", typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(mark, "Create color checkmark");
                mark.transform.SetParent(button.transform, false);
                Image background = mark.GetComponent<Image>();
                background.color = new Color(.12f, .065f, .025f, 1);
                background.raycastTarget = false;
                RectTransform rect = background.rectTransform;
                rect.anchorMin = rect.anchorMax = Vector2.one;
                rect.anchoredPosition = new Vector2(5, -2);
                rect.sizeDelta = new Vector2(30, 30);
                AddStroke(rect, new Vector2(-6, -1), new Vector2(11, 4), -45);
                AddStroke(rect, new Vector2(3, 2), new Vector2(19, 4), 45);
                Undo.RecordObject(button, "Connect color checkmark");
                button.ConfigureSelectionMark(mark);
            }
            button.RefreshSelection(PlayerColorId.Blue);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(group);
        // проверяем, что для каждого варианта видна ровно одна галочка, и возвращаем исходный синий цвет.
        foreach (ColorButton selected in buttons)
        {
            foreach (ColorButton button in buttons) button.RefreshSelection(selected.ColorId);
            int visible = buttons.Count(button => ((GameObject)new SerializedObject(button)
                .FindProperty("selectedMark").objectReferenceValue).activeSelf);
            if (visible != 1) throw new InvalidOperationException("Expected exactly one selected color mark.");
        }
        foreach (ColorButton button in buttons) button.RefreshSelection(PlayerColorId.Blue);
        File.WriteAllText("Logs/color-selection-validation.txt", "PASS: exactly one checkmark for each of ten color choices.");
        MenuShelfInspection.Inspect();
    }

    // рисуем галочку двумя полосками, чтобы её вид не зависел от наличия символа в шрифте.
    private static void AddStroke(RectTransform parent, Vector2 position, Vector2 size, float angle)
    {
        var stroke = new GameObject("Checkmark stroke", typeof(RectTransform), typeof(Image));
        stroke.transform.SetParent(parent, false);
        Image image = stroke.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
        image.rectTransform.anchoredPosition = position;
        image.rectTransform.sizeDelta = size;
        image.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
    }
}
