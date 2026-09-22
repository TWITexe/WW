using System;
using System.IO;
using System.Linq;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// подключает метель, огненный вихрь и попадания, сохраняя исходники пакета и сетевые корни.
public static class AreaAndHitEffectsBuilder
{
    private const string Pack = "Assets/Hovl Studio/Magic effects pack/Prefabs/";
    private const string Output = "Assets/Resources/SpellHits";
    private const string Report = "Logs/AreaAndHitEffects";
    private static readonly string[] HitNames = { "Holy hit", "Explosion", "Snow hit", "Stones hit" };

    // установку можно повторить: заменяются только созданные этим инструментом визуальные дочерние объекты.
    [MenuItem("Tools/Wizard War/Install area and hit effects")]
    public static void Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before installing effects.");
        Directory.CreateDirectory(Output);
        Directory.CreateDirectory(Report);
        AssetDatabase.Refresh();
        InstallArea("Blizzard", "Magic circles/Freeze circle", false);
        InstallArea("FireTornado", "Smoke effects/Smoke vortex", true);
        InstallHits();
        AssignHits();
        AssetDatabase.SaveAssets();
        Validate();
    }

    // обновляем только торнадо, не перезаписывая уже настроенные метель и попадания.
    [MenuItem("Tools/Wizard War/Enlarge fire tornado")]
    public static void EnlargeTornado()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before editing the prefab.");
        InstallArea("FireTornado", "Smoke effects/Smoke vortex", true);
        AssetDatabase.SaveAssets();
        Validate();
    }
    // резервная копия сохраняется только перед первой заменой, чтобы повторная установка её не затёрла.
    private static void Backup(string path)
    {
        string target = Report + "/" + Path.GetFileName(path);
        if (!File.Exists(target)) File.Copy(path, target);
    }

    // длительность задаётся способностью; графика удаляется вместе с сетевой областью на всех клиентах.
    private static void InstallArea(string name, string sourceName, bool tornado)
    {
        string path = "Assets/Other Asstets/GeneratedWizard/" + name + ".prefab";
        var spell = AssetDatabase.LoadAssetAtPath<ElementalSpell>("Assets/Scripts/Spells/Elemental/" + name + ".asset");
        Backup(path);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
            {
                if (child.name == "Imported area effect") UnityEngine.Object.DestroyImmediate(child.gameObject);
                else child.gameObject.SetActive(false);
            }
            if (root.TryGetComponent<ElementalVisual>(out var oldVisual)) oldVisual.enabled = false;
            if (root.TryGetComponent<SpellVfx>(out var oldVfx)) oldVfx.enabled = false;
            foreach (var renderer in root.GetComponents<Renderer>()) renderer.enabled = false;
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Pack + sourceName + ".prefab"), root.transform);
            visual.name = "Imported area effect";
            visual.transform.localPosition = tornado ? Vector3.zero : Vector3.down * .2f;
            visual.transform.localRotation = Quaternion.identity;
            // оба исходника рассчитаны на радиус четыре; подгоняем только графику под игровую область.
            visual.transform.localScale = (tornado ? new Vector3(1.35f, 3f, 1.35f) : Vector3.one) * (spell.radius / 4f);
            Prepare(visual);
            foreach (var particles in visual.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.loop = true;
                main.duration = spell.duration;
                main.startDelay = 0;
                var emission = particles.emission;
                if (tornado)
                {
                    bool smoke = particles.gameObject == visual;
                    main.prewarm = true;
                    main.maxParticles = smoke ? 20 : 8;
                    emission.rateOverTime = smoke ? 7 : 3;
                    emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    main.startColor = smoke
                        ? new ParticleSystem.MinMaxGradient(new Color(.75f, .12f, .025f, .5f), new Color(1, .42f, .06f, .65f))
                        : new ParticleSystem.MinMaxGradient(new Color(1, .3f, .025f, .55f));
                    NeutralizeGradient(particles);
                }
                else if (particles.gameObject == visual || particles.name == "Sides")
                {
                    main.startLifetime = spell.duration;
                    main.maxParticles = 2;
                    emission.rateOverTime = 0;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)1) });
                }
                else if (particles.name == "Snowflakes" || particles.name == "Sparks")
                {
                    main.maxParticles = 40;
                    emission.rateOverTime = particles.name == "Snowflakes" ? 24 : 16;
                    emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                }
            }
            var guard = root.GetComponent<ImportedAreaVisual>();
            if (guard == null) guard = root.AddComponent<ImportedAreaVisual>();
            guard.Configure(visual);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // создаём компактные одноразовые варианты и сохраняем время их безопасного удаления.
    private static void InstallHits()
    {
        const string libraryPath = "Assets/Resources/SpellHitLibrary.asset";
        var library = AssetDatabase.LoadAssetAtPath<SpellHitLibrary>(libraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<SpellHitLibrary>();
            AssetDatabase.CreateAsset(library, libraryPath);
        }
        library.prefabs = new GameObject[HitNames.Length];
        library.lifetimes = new float[HitNames.Length];
        for (int index = 0; index < HitNames.Length; index++)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Hits and explosions/" + HitNames[index] + ".prefab"));
            try
            {
                instance.name = HitNames[index];
                instance.transform.position = Vector3.zero;
                instance.transform.localScale = Vector3.one * .25f;
                Prepare(instance);
                float lifetime = 0;
                foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.loop = false;
                    main.prewarm = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.maxParticles = 64;
                    var emission = particles.emission;
                    emission.rateOverTime = 0;
                    // оставляем исходные короткие выбросы, но ограничиваем каждый из них.
                    var bursts = new ParticleSystem.Burst[emission.burstCount];
                    emission.GetBursts(bursts);
                    for (int i = 0; i < bursts.Length; i++)
                    {
                        bursts[i].count = new ParticleSystem.MinMaxCurve(Mathf.Min(48, bursts[i].count.constantMax));
                        bursts[i].cycleCount = 1;
                    }
                    emission.SetBursts(bursts);
                    lifetime = Mathf.Max(lifetime, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax + .25f);
                    if (index == (int)SpellHitKind.Holy)
                    {
                        main.startColor = new Color(1, 1, 1, main.startColor.color.a);
                        NeutralizeGradient(particles);
                    }
                }
                library.prefabs[index] = PrefabUtility.SaveAsPrefabAsset(instance, Output + "/" + HitNames[index] + ".prefab");
                library.lifetimes[index] = lifetime;
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        EditorUtility.SetDirty(library);
    }

    // назначаем тип явно: смешанный рецепт не всегда определяет внешний вид способности.
    private static void AssignHits()
    {
        foreach (string folder in new[] { "Assets/Scripts/Spells/Elemental", "Assets/Scripts/Spells/Tactical" })
        foreach (string path in Directory.GetFiles(folder, "*.asset"))
        {
            var spell = AssetDatabase.LoadAssetAtPath<Spell>(path.Replace('\\', '/'));
            if (spell == null) continue;
            Backup(path);
            switch (spell.name)
            {
                case "FireSeal": case "FireTornado": case "Magma": case "BoilingJet":
                    spell.hitEffect = SpellHitKind.Fire; break;
                case "IceShard": case "FrostNova": case "Blizzard": case "IceMirror": case "SnowDecoy":
                    spell.hitEffect = SpellHitKind.Snow; break;
                case "Boulder": case "Mud": case "StoneWall": case "StoneSkin":
                    spell.hitEffect = SpellHitKind.Stone; break;
                default: spell.hitEffect = SpellHitKind.Holy; break;
            }
            EditorUtility.SetDirty(spell);
        }
    }

    // исходные текстуры и прозрачность сохраняются в отдельных материалах urp, без дополнительных теней и света.
    private static void Prepare(GameObject root)
    {
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = true;
            main.stopAction = ParticleSystemStopAction.None;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var lights = particles.lights;
            lights.enabled = false;
            if (particles.name == "Light") particles.gameObject.SetActive(false);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterials = renderer.sharedMaterials.Select(IceAndShieldEffectsBuilder.ConvertMaterial).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
    }

    // убираем старый оттенок из градиента, сохраняя авторское плавное исчезновение частиц.
    private static void NeutralizeGradient(ParticleSystem particles)
    {
        var colors = particles.colorOverLifetime;
        if (!colors.enabled) return;
        Gradient original = colors.color.gradient;
        var neutral = new Gradient();
        neutral.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, original.alphaKeys);
        colors.color = neutral;
    }

    // проверяем ссылки и жизнь областей, а также отсутствие зацикленных попаданий.
    [MenuItem("Tools/Wizard War/Validate area and hit effects")]
    public static void Validate()
    {
        var report = new StringBuilder();
        foreach (string name in new[] { "Blizzard", "FireTornado" })
        {
            var spell = AssetDatabase.LoadAssetAtPath<ElementalSpell>("Assets/Scripts/Spells/Elemental/" + name + ".asset");
            if (spell.effectPrefab.GetComponent<NetworkIdentity>() == null) throw new InvalidOperationException("Missing network root.");
            var systems = spell.effectPrefab.GetComponentsInChildren<ParticleSystem>();
            if (systems.Length == 0 || systems.Any(p => !p.main.loop || p.lights.enabled)) throw new InvalidOperationException("Invalid area particles.");
            Preview(spell.effectPrefab, name, 1.2f, true);
            report.AppendLine($"PASS {name}: duration={spell.duration}, radius={spell.radius}, particles={systems.Length}");
        }
        var library = AssetDatabase.LoadAssetAtPath<SpellHitLibrary>("Assets/Resources/SpellHitLibrary.asset");
        for (int i = 0; i < library.prefabs.Length; i++)
        {
            var systems = library.prefabs[i].GetComponentsInChildren<ParticleSystem>();
            if (systems.Length == 0 || systems.Any(p => p.main.loop || p.lights.enabled || p.main.maxParticles > 64))
                throw new InvalidOperationException("Invalid hit particles.");
            Preview(library.prefabs[i], HitNames[i], .12f, false);
            report.AppendLine($"PASS {HitNames[i]}: cleanup={library.lifetimes[i]}");
        }
        File.WriteAllText(Report + "/validation.txt", report.ToString());
    }

    // снимаем отдельную сцену предпросмотра; пользовательскую сцену и её несохранённые изменения не трогаем.
    private static void Preview(GameObject prefab, string name, float time, bool area)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Scene scene = EditorSceneManager.NewPreviewScene();
        var target = new RenderTexture(800, 600, 24);
        var image = new Texture2D(800, 600, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var cameraObject = new GameObject("Effect preview", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = area ? new Vector3(0, 6, -10) : new Vector3(0, 2, -4);
            camera.transform.LookAt(area ? Vector3.up : Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .09f, .11f);
            camera.targetTexture = target;
            var systems = instance.GetComponentsInChildren<ParticleSystem>();
            foreach (var system in systems) system.Simulate(time, false, true);
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 800, 600), 0, 0);
            image.Apply();
            File.WriteAllBytes(Report + "/" + name + ".png", image.EncodeToPNG());
            if (area)
            {
                float end = prefab.GetComponent<ElementalEffect>().definition.duration - .2f;
                foreach (var system in systems) system.Simulate(end, false, true);
                if (systems.Sum(p => p.particleCount) == 0) throw new InvalidOperationException("Area disappeared early.");
            }
            camera.targetTexture = null;
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
