using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// копирует геометрию эмблем с закрытых книг в компактные ассеты для интерфейса страниц.
public static class BookSymbolBuilder
{
    // выполняем перенос только по явной команде и сохраняем текущую расстановку сцены.
    [MenuItem("Tools/Wizard War/Use cover symbols on open books")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        Transform[] objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var menu = objects.Select(item => item.GetComponent<InteractiveShelfMenu>()).Single(item => item != null);
        string[] names = { "Fire book", "Air book", "Ice book", "Earth book (1)", "Water book" };
        var symbols = new Mesh[5];
        Directory.CreateDirectory("Assets/UI/BookSymbols");
        AssetDatabase.Refresh();
        for (int index = 0; index < names.Length; index++)
        {
            Transform book = objects.Single(item => item.name == names[index]);
            MeshFilter emblem = book.GetComponentsInChildren<MeshFilter>(true).Single(item =>
                item.name.EndsWith("icon", StringComparison.OrdinalIgnoreCase) || item.name == "Water drop emblem");
            Mesh generated = ProjectEmblem(book, emblem);
            string path = "Assets/UI/BookSymbols/" + (MagicElement)index + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(generated, path);
            else
            {
                EditorUtility.CopySerialized(generated, existing);
                UnityEngine.Object.DestroyImmediate(generated);
            }
            symbols[index] = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        Directory.CreateDirectory("Logs/InteractiveShelfMenu");
        EditorSceneManager.SaveScene(scene);
        string backupPath = "Logs/InteractiveShelfMenu/Menu-before-cover-symbols.unity";
        if (!File.Exists(backupPath)) File.Copy(scene.path, backupPath);
        foreach (OpenBookSlot slot in menu.OpenBookSlots)
        {
            var icon = slot.GetComponentInChildren<BookSymbolGraphic>(true);
            if (icon == null)
            {
                SpellIconGraphic previous = slot.GetComponentInChildren<SpellIconGraphic>(true);
                var root = new GameObject("Cover element symbol", typeof(RectTransform), typeof(BookSymbolGraphic));
                Undo.RegisterCreatedObjectUndo(root, "Create cover symbol");
                root.transform.SetParent(previous.transform.parent, false);
                icon = root.GetComponent<BookSymbolGraphic>();
                icon.rectTransform.sizeDelta = previous.rectTransform.sizeDelta;
                icon.rectTransform.localPosition = previous.rectTransform.localPosition;
                icon.raycastTarget = false;
                Undo.RecordObject(previous.gameObject, "Hide old spell symbol");
                previous.gameObject.SetActive(false);
            }
            if (icon.GetComponent<CanvasRenderer>() == null)
                Undo.AddComponent<CanvasRenderer>(icon.gameObject);
            // небольшой отступ от бумаги не даёт изгибу страницы закрывать плоскую эмблему.
            Vector3 position = icon.rectTransform.localPosition;
            position.z = -10f;
            icon.rectTransform.localPosition = position;
            Undo.RecordObject(slot, "Connect cover symbols");
            slot.ConfigureBookSymbols(icon, symbols);
            slot.Refresh(ElementLoadout.Default, 0);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        OpenBookSlotBuilder.Validate();
        InteractiveShelfMenuBuilder.Validate();
        File.WriteAllText("Logs/book-symbols-validation.txt", "PASS: five cover meshes projected; three page symbols connected; scene saved.");
    }

    // проецируем саму эмблему вдоль нормали обложки, сохраняя пропорции и цвета её материалов.
    private static Mesh ProjectEmblem(Transform book, MeshFilter emblem)
    {
        Mesh source = emblem.sharedMesh;
        Vector3[] sourceVertices = source.vertices;
        var projected = new Vector3[sourceVertices.Length];
        for (int index = 0; index < sourceVertices.Length; index++)
        {
            Vector3 point = book.InverseTransformPoint(emblem.transform.TransformPoint(sourceVertices[index]));
            projected[index] = new Vector3(-point.x, -point.z, 0);
        }
        Bounds bounds = new Bounds(projected[0], Vector3.zero);
        foreach (Vector3 point in projected) bounds.Encapsulate(point);
        float size = Mathf.Max(bounds.size.x, bounds.size.y);
        if (size < .00001f) throw new InvalidOperationException("Cover symbol has no visible area.");
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        Material[] materials = emblem.GetComponent<Renderer>().sharedMaterials;
        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            Material material = materials[submesh];
            Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            int[] indices = source.GetTriangles(submesh);
            for (int index = 0; index < indices.Length; index += 3)
            {
                Vector3 a = (projected[indices[index]] - bounds.center) / size * .9f;
                Vector3 b = (projected[indices[index + 1]] - bounds.center) / size * .9f;
                Vector3 c = (projected[indices[index + 2]] - bounds.center) / size * .9f;
                float area = Vector3.Cross(b - a, c - a).z;
                if (Mathf.Abs(area) < .0000001f) continue;
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                colors.Add(color); colors.Add(color); colors.Add(color);
                triangles.Add(start);
                triangles.Add(start + (area < 0 ? 1 : 2));
                triangles.Add(start + (area < 0 ? 2 : 1));
            }
        }
        var result = new Mesh { name = book.name + " cover symbol" };
        result.SetVertices(vertices);
        result.SetColors(colors);
        result.SetTriangles(triangles, 0);
        result.RecalculateBounds();
        return result;
    }
}
