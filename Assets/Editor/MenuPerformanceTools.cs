using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// диагностирует и ограничивает стоимость декоративных теней в меню, не меняя яркость источников.
public static class MenuPerformanceTools
{
    // отдельный пакетный запуск компилирует проект и применяет настройки к сохранённому меню.
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
        Optimize();
        InteractiveShelfMenuBuilder.Validate();
    }
    // все обходы сцены выполняются только по команде редактора, а не каждый кадр игры.
    [MenuItem("Tools/Wizard War/Optimize menu lighting")]
    public static void Optimize()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
            throw new InvalidOperationException("Open Menu outside Play Mode.");
        Light[] lights = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
        var report = new StringBuilder();
        report.AppendLine("Menu lighting: before");
        Describe(lights, report);
        Directory.CreateDirectory("Logs/InteractiveShelfMenu");
        EditorSceneManager.SaveScene(scene);
        const string backup = "Logs/InteractiveShelfMenu/Menu-before-optimization.unity";
        if (!File.Exists(backup)) File.Copy(scene.path, backup);

        // у точечного света шесть направлений теневого рендера; оставляем тени двум ближайшим к столу источникам.
        Vector3 desk = new Vector3(-3.8f, .8f, -8.2f);
        Light[] keyLights = lights.Where(light => light.isActiveAndEnabled &&
                light.type != LightType.Directional && light.shadows != LightShadows.None)
            .OrderBy(light => (light.transform.position - desk).sqrMagnitude).Take(2).ToArray();
        foreach (Light light in lights)
        {
            if (light.type == LightType.Directional || light.shadows == LightShadows.None) continue;
            Undo.RecordObject(light, "Optimize decorative light shadows");
            if (keyLights.Contains(light))
            {
                light.shadows = LightShadows.Hard;
                light.shadowResolution = LightShadowResolution.Low;
                light.shadowCustomResolution = 256;
            }
            else light.shadows = LightShadows.None;
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
        }
        report.AppendLine("Menu lighting: after");
        Describe(lights, report);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/menu-performance.txt", report.ToString());
        Debug.Log("MENU_LIGHTING_OPTIMIZED: " + keyLights.Length + " local shadow lights retained.");
    }

    // считаем потенциальные карты теней; фактическое число зависит от отсечения камерой и рендерера.
    private static void Describe(Light[] lights, StringBuilder report)
    {
        Light[] active = lights.Where(light => light.isActiveAndEnabled).ToArray();
        Light[] shadows = active.Where(light => light.shadows != LightShadows.None).ToArray();
        int localMaps = shadows.Where(light => light.type != LightType.Directional)
            .Sum(light => light.type == LightType.Point ? 6 : 1);
        report.AppendLine($"Active lights: {active.Length}; shadow lights: {shadows.Length}; potential local shadow maps: {localMaps}");
        foreach (Light light in shadows)
            report.AppendLine($"{light.name}: {light.type}, {light.shadows}, position={light.transform.position}, range={light.range}");
    }
}
