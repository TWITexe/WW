using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// сохраняет настройки освещения арены, постобработки и материалов в проект.
public static class ArenaLightingPreset
{
    const string Folder = "Assets/Settings/ArenaLighting";
    static readonly HashSet<string> changed = new HashSet<string>();
    // открываем арену, настраиваем солнце, небо и эффекты, затем сохраняем сцену и изменённые ассеты.
    [MenuItem("Wizard/Lighting/Apply arena lighting to saved arena")]
    public static void Apply()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        changed.Clear();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Settings", "ArenaLighting");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(48, -32, 0);
        sun.color = new Color(1f, .91f, .78f);
        sun.intensity = 2.1f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .82f;
        sun.shadowBias = .035f;
        sun.shadowNormalBias = .3f;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.40f, .51f, .68f);
        RenderSettings.ambientEquatorColor = new Color(.24f, .30f, .40f);
        RenderSettings.ambientGroundColor = new Color(.16f, .15f, .19f);
        RenderSettings.reflectionIntensity = .65f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.59f, .69f, .78f);
        RenderSettings.fogStartDistance = 65;
        RenderSettings.fogEndDistance = 180;
        string skyPath = Folder + "/ArenaSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
        sky.SetColor("_SkyTint", new Color(.51f, .57f, .67f));
        sky.SetColor("_GroundColor", new Color(.30f, .32f, .37f));
        sky.SetFloat("_Exposure", 1.05f);
        sky.SetFloat("_AtmosphereThickness", .85f);
        sky.SetFloat("_SunSize", .035f);
        RenderSettings.skybox = sky; Mark(sky);

        string profilePath = Folder + "/ArenaLook.asset";
        // профиль хранится отдельным ассетом, чтобы настройки постобработки можно было редактировать в инспекторе.
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath); }
        var bloom = Component<Bloom>(profile);
        bloom.threshold.Override(1.15f); bloom.intensity.Override(.22f); bloom.scatter.Override(.55f);
        bloom.highQualityFiltering.Override(false);
        var tone = Component<Tonemapping>(profile); tone.mode.Override(TonemappingMode.Neutral);
        var color = Component<ColorAdjustments>(profile);
        color.postExposure.Override(0); color.contrast.Override(6); color.saturation.Override(4);
        Mark(profile);
        var volume = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).FirstOrDefault(v => v.isGlobal);
        if (volume == null) volume = new GameObject("Arena Lighting Volume").AddComponent<Volume>();
        volume.isGlobal = true; volume.weight = 1; volume.sharedProfile = profile;
        foreach (string name in new[] { "FireTornado", "Magma", "BoilingJet" })
        {
            Glow(AssetDatabase.LoadAssetAtPath<Material>("Assets/GeneratedWizard/" + name + ".mat"), 2.5f);
            Glow(AssetDatabase.LoadAssetAtPath<Material>("Assets/GeneratedWizard/" + name + "Particles.mat"), 2f);
        }
        const string fireballPath = "Assets/Prefabs/FireBall.prefab";
        // создаём локальные копии материалов фаерболла, сохраняя исходные материалы импортированных ресурсов.
        var fireball = PrefabUtility.LoadPrefabContents(fireballPath);
        try
        {
            foreach (var renderer in fireball.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    string path = Folder + "/Fireball_" + materials[i].name + ".mat";
                    if (AssetDatabase.GetAssetPath(materials[i]).StartsWith(Folder + "/")) path = AssetDatabase.GetAssetPath(materials[i]);
                    var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (copy == null) { copy = new Material(materials[i]); AssetDatabase.CreateAsset(copy, path); }
                    Glow(copy, 2.5f); materials[i] = copy;
                }
                renderer.sharedMaterials = materials;
            }
            PrefabUtility.SaveAsPrefabAsset(fireball, fireballPath); changed.Add(fireballPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(fireball); }
        Polish("Wizard_Trim _ old gold", .7f, .55f);
        Polish("Wizard_Staff _ white crystal", .15f, .7f);
        EditorSceneManager.SaveScene(scene); changed.Add(scene.path);
        AssetDatabase.SaveAssets();
        if (!profile.TryGet<Bloom>(out var check) || check.intensity.value != .22f || RenderSettings.sun != sun)
            throw new Exception("Arena lighting validation failed");
        Directory.CreateDirectory("Logs");
        File.WriteAllLines("Logs/arena-lighting-files.txt", changed.OrderBy(p => p));
        Debug.Log("ARENA_LIGHTING_APPLIED_AND_VALIDATED: " + changed.Count + " assets; HDR bloom, sun, sky and materials");
    }
    // получаем эффект из профиля постобработки или добавляем его как вложенный ассет.
    static T Component<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var component)) return component;
        component = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(component, profile); return component;
    }
    // отмечаем ассет изменённым и записываем его путь для итогового отчёта.
    static void Mark(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); changed.Add(AssetDatabase.GetAssetPath(asset)); }
    // сохраняем оттенок материала, нормируя яркость свечения к заданному уровню.
    static void Glow(Material material, float strength)
    {
        if (material == null || !AssetDatabase.GetAssetPath(material).StartsWith("Assets/") || !material.HasProperty("_BaseColor")) return;
        Color color = material.GetColor("_BaseColor");
        float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        if (peak < .001f) return;
        Color hdr = new Color(color.r / peak * strength, color.g / peak * strength, color.b / peak * strength, color.a);
        if (material.HasProperty("_EmissionColor")) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", hdr); }
        else material.SetColor("_BaseColor", hdr);
        Mark(material);
    }
    // задаём металлический блеск и гладкость выбранного материала мага.
    static void Polish(string name, float metal, float smooth)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GeneratedWizard/" + name + ".mat");
        if (material == null) return;
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metal);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smooth);
        Mark(material);
    }
}
