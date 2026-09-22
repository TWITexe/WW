using System.IO;
using System.Text;
using System;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// исследует и настраивает импортированный магический круг для огненной печати.
public static class FireSealCircleBuilder
{
    private const string SourcePath = "Assets/Hovl Studio/Magic effects pack/Prefabs/Magic circles/Magic circle 2.prefab";
    private const string TargetPath = "Assets/TacticalSpells/FireSeal.prefab";
    private const string MaterialFolder = "Assets/TacticalSpells/FireSealCircleMaterials";

    // меняем только визуальную часть существующего сетевого префаба и сохраняем исходник пакета нетронутым.
    [MenuItem("Tools/Wizard War/Install fire seal circle")]
    public static void Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Install outside Play Mode.");
        Directory.CreateDirectory("Logs/FireSealCircle");
        if (!File.Exists("Logs/FireSealCircle/FireSeal-before.prefab"))
            File.Copy(TargetPath, "Logs/FireSealCircle/FireSeal-before.prefab");
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) throw new InvalidOperationException("URP particle shader missing.");
        GameObject root = PrefabUtility.LoadPrefabContents(TargetPath);
        try
        {
            if (root.GetComponent<FireSealCircleVisual>() != null)
                throw new InvalidOperationException("Fire seal circle already installed.");
            foreach (Transform child in root.transform) child.gameObject.SetActive(false);
            root.GetComponent<TacticalVisual>().enabled = false;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var circle = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            circle.name = "Fire seal - Magic circle 2";
            circle.transform.localPosition = Vector3.zero;
            circle.transform.localRotation = Quaternion.identity;
            // исходный круг имеет диаметр четыре метра; видимый край отмечает радиус срабатывания 1,25 м.
            circle.transform.localScale = Vector3.one * .625f;
            foreach (ParticleSystem particles in circle.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.stopAction = ParticleSystemStopAction.None;
                main.loop = true;
                main.playOnAwake = true;
                main.maxParticles = particles.gameObject == circle ? 4 : particles.name == "Sides" ? 12 : 48;
                main.startColor = particles.name == "Sparks" ? new Color(1, .55f, .09f) : new Color(1, .22f, .025f);
                var colors = particles.colorOverLifetime;
                if (colors.enabled)
                {
                    // сохраняем затухание частиц, заменяя фиолетовые оттенки нейтральным множителем.
                    Gradient original = colors.color.gradient;
                    var gradient = new Gradient();
                    gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, original.alphaKeys);
                    colors.color = gradient;
                }
                if (particles.name == "Sparks")
                {
                    main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 1.4f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .9f);
                    var emission = particles.emission;
                    emission.rateOverTime = 18;
                }
                // световые частицы и тёмные искры не нужны для читаемой наземной ловушки.
                if (particles.name == "Light" || particles.name == "DarkSparks") particles.gameObject.SetActive(false);
                var lights = particles.lights;
                lights.enabled = false;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = MakeMaterial(renderer.sharedMaterial, shader);
            }
            var visual = root.AddComponent<FireSealCircleVisual>();
            visual.Configure(root.GetComponent<TacticalEffect>(), circle, circle.GetComponentsInChildren<Renderer>());
            PrefabUtility.SaveAsPrefabAsset(root, TargetPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Validate();
    }

    // создаём собственные материалы urp с текстурами пакета, не перекрашивая его остальные эффекты.
    private static Material MakeMaterial(Material source, Shader shader)
    {
        string path = MaterialFolder + "/" + source.name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = "FireSeal " + source.name, renderQueue = 3000 };
        material.SetTexture("_BaseMap", source.mainTexture);
        material.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.6f, 1));
        material.SetFloat("_Surface", 1);
        material.SetFloat("_Blend", 0);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetShaderPassEnabled("ShadowCaster", false);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // проверяем механику, сетевой корень и продолжительность визуального эффекта без запуска матча.
    [MenuItem("Tools/Wizard War/Validate fire seal circle")]
    public static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath);
        var spell = AssetDatabase.LoadAssetAtPath<TacticalSpell>("Assets/Scripts/Spells/Tactical/FireSeal.asset");
        if (prefab.GetComponent<NetworkIdentity>() == null || prefab.GetComponent<TacticalEffect>().definition != spell
            || spell.effectPrefab != prefab || spell.duration != 9 || spell.damage != 35 || spell.radius != 3)
            throw new InvalidOperationException("Fire seal gameplay or network references changed.");
        var visual = prefab.GetComponent<FireSealCircleVisual>();
        if (visual == null) throw new InvalidOperationException("Circle visual missing.");
        foreach (var particles in prefab.GetComponentsInChildren<ParticleSystem>())
        {
            if (!particles.main.loop || particles.lights.enabled)
                throw new InvalidOperationException("Seal must remain visible without particle lights.");
            var material = particles.GetComponent<Renderer>().sharedMaterial;
            if (material.shader.name != "Universal Render Pipeline/Particles/Unlit")
                throw new InvalidOperationException("Particle material must support URP.");
        }
        CapturePreview(prefab);
        File.WriteAllText("Logs/fire-seal-circle-validation.txt", "PASS: original network prefab; damage 35, radius 3, duration 9 unchanged; looping URP particles; visible at 8.5 seconds; no particle lights or shadows.");
    }

    // снимаем круг в отдельной временной сцене, не добавляя тестовые объекты на арену.
    private static void CapturePreview(GameObject prefab)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(800, 600, 24);
        var image = new Texture2D(800, 600, TextureFormat.RGB24, false);
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var cameraObject = new GameObject("Preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = new Vector3(0, 3.8f, -4);
            camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .06f, .075f);
            camera.targetTexture = target;
            ParticleSystem circle = instance.GetComponentsInChildren<ParticleSystem>().Single(item => item.name == "Fire seal - Magic circle 2");
            circle.Simulate(1.5f, true, true);
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 800, 600), 0, 0);
            image.Apply();
            File.WriteAllBytes("Logs/FireSealCircle/preview.png", image.EncodeToPNG());
            circle.Simulate(8.5f, true, true);
            if (circle.particleCount == 0) throw new InvalidOperationException("Circle disappeared before seal expires.");
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // читаем размеры и материалы исходника, не изменяя пакет эффектов.
    [MenuItem("Tools/Wizard War/Inspect fire seal circle")]
    public static void Inspect()
    {
        var report = new StringBuilder();
        foreach (string path in new[] { SourcePath,
            "Assets/Hovl Studio/Magic effects pack/Prefabs/Environment/Crystal effect blue.prefab",
            "Assets/Hovl Studio/Magic effects pack/Prefabs/Magic shields/Magic shield gray.prefab", "Assets/Hovl Studio/Magic effects pack/Prefabs/Magic circles/Freeze circle.prefab", "Assets/Hovl Studio/Magic effects pack/Prefabs/Smoke effects/Smoke vortex.prefab", "Assets/Hovl Studio/Magic effects pack/Prefabs/Hits and explosions/Explosion.prefab", "Assets/Hovl Studio/Magic effects pack/Prefabs/Hits and explosions/Snow hit.prefab", "Assets/Hovl Studio/Magic effects pack/Prefabs/Hits and explosions/Stones hit.prefab", "Assets/Hovl Studio/Magic effects pack/Prefabs/Hits and explosions/Holy hit.prefab" })
        {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        report.AppendLine("PREFAB " + path);
        foreach (var particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particles.main;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            report.AppendLine($"{particles.name}: size={main.startSize.constantMax} lifetime={main.startLifetime.constantMax} speed={main.startSpeed.constantMax} max={main.maxParticles} rotation={main.startRotation.constantMax} rotation3D={main.startRotation3D} rotX={main.startRotationX.constantMax} color={main.startColor.color} emission={particles.emission.rateOverTime.constantMax} shape={particles.shape.shapeType} radius={particles.shape.radius} mode={renderer.renderMode} align={renderer.alignment} local={particles.transform.localPosition} rot={particles.transform.localEulerAngles}");
            foreach (var material in renderer.sharedMaterials)
                report.AppendLine($"material={material.name} shader={material.shader.name} texture={material.mainTexture?.name}");
            if (renderer.mesh != null) report.AppendLine($"mesh={renderer.mesh.name} bounds={renderer.mesh.bounds}");
        }
        }
        File.WriteAllText("Logs/fire-seal-circle-inspection.txt", report.ToString());
    }
}
