using System;
using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// подключает эффекты пакета к ледяному копью и каменной коже, сохраняя игровые параметры.
public static class IceAndShieldEffectsBuilder
{
    private const string IcePath = "Assets/Other Asstets/GeneratedWizard/IceShard.prefab";
    private const string PlayerPath = "Assets/Prefabs/Player.prefab";
    private const string ShieldPath = "Assets/Other Asstets/GeneratedWizard/StoneSkinShield.prefab";
    private const string MaterialsPath = "Assets/Other Asstets/GeneratedWizard/ImportedSpellMaterials";
    private const string PackPath = "Assets/Hovl Studio/Magic effects pack/Prefabs/";

    // делаем резервные копии и обновляем существующие префабы без замены сетевого корня снаряда.
    [MenuItem("Tools/Wizard War/Install ice and shield effects")]
    public static void Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Install outside Play Mode.");
        Directory.CreateDirectory("Logs/IceAndShieldEffects");
        foreach (string path in new[] { IcePath, PlayerPath, "Assets/Scripts/Spells/Elemental/StoneSkin.asset" })
        {
            string backup = "Logs/IceAndShieldEffects/" + Path.GetFileName(path);
            if (!File.Exists(backup)) File.Copy(path, backup);
        }
        Directory.CreateDirectory(MaterialsPath);
        AssetDatabase.Refresh();
        InstallIce();
        InstallShield();
        AssetDatabase.SaveAssets();
        Validate();
    }

    // поворачиваем вертикальный кристалл вдоль локальной оси полёта z, не вращая физический снаряд.
    private static void InstallIce()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(IcePath);
        try
        {
            if (root.transform.Cast<Transform>().Any(child => child.name == "Ice spear - Crystal effect blue"))
                return;
            foreach (Transform child in root.transform) child.gameObject.SetActive(false);
            root.GetComponent<ElementalVisual>().enabled = false;
            if (root.TryGetComponent<SpellVfx>(out var oldVfx)) oldVfx.enabled = false;
            foreach (Renderer renderer in root.GetComponents<Renderer>()) renderer.enabled = false;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PackPath + "Environment/Crystal effect blue.prefab");
            var crystal = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            crystal.name = "Ice spear - Crystal effect blue";
            crystal.transform.localPosition = Vector3.zero;
            crystal.transform.localRotation = Quaternion.Euler(90, 0, 0);
            crystal.transform.localScale = Vector3.one;
            PrepareParticles(crystal);
            ParticleSystem core = crystal.GetComponent<ParticleSystem>();
            var main = core.main;
            main.loop = false;
            main.startLifetime = 5;
            main.startSpeed = 0;
            main.startRotation = 0;
            main.maxParticles = 1;
            var shape = core.shape;
            shape.enabled = false;
            var size = core.sizeOverLifetime;
            size.enabled = false;
            var rotation = core.rotationOverLifetime;
            rotation.enabled = false;
            var colors = core.colorOverLifetime;
            colors.enabled = false;
            // основная частица появляется сразу: быстрый снаряд не должен ждать анимации роста кристалла.
            var emission = core.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)1) });
            foreach (ParticleSystem particles in crystal.GetComponentsInChildren<ParticleSystem>())
            {
                if (particles == core) continue;
                var childMain = particles.main;
                childMain.loop = true;
                if (particles.name == "Sparks")
                {
                    var sparks = particles.emission;
                    sparks.rateOverTime = 24;
                    childMain.simulationSpace = ParticleSystemSimulationSpace.World;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, IcePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // эффект щита следует за игроком и живёт по синхронизированному запасу защиты, включая досрочное разрушение.
    private static void InstallShield()
    {
        var spell = AssetDatabase.LoadAssetAtPath<ElementalSpell>("Assets/Scripts/Spells/Elemental/StoneSkin.asset");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PackPath + "Magic shields/Magic shield gray.prefab");
        var shield = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            shield.name = "StoneSkinShield";
            PrepareParticles(shield);
            foreach (ParticleSystem particles in shield.GetComponentsInChildren<ParticleSystem>())
            {
                var main = particles.main;
                main.loop = true;
                main.duration = spell.duration;
                // основные оболочки сохраняются весь срок защиты; короткие декоративные следы остаются короткими.
                if (particles.name != "Trails") main.startLifetime = spell.duration;
            }
            spell.effectPrefab = PrefabUtility.SaveAsPrefabAsset(shield, ShieldPath);
            EditorUtility.SetDirty(spell);
        }
        finally { UnityEngine.Object.DestroyImmediate(shield); }
        GameObject player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var controller = player.GetComponentInChildren<CharacterController>(true);
            var health = player.GetComponentInChildren<Health>(true);
            if (controller == null || health == null)
                throw new InvalidOperationException("Player controller or health is missing.");
            var visual = controller.GetComponent<StoneSkinShieldVisual>();
            if (visual == null) visual = controller.gameObject.AddComponent<StoneSkinShieldVisual>();
            // в исходном эффекте сфера расположена на метр выше основания, поэтому корень ставим у ног.
            Vector3 feet = visual.transform.InverseTransformPoint(controller.transform.TransformPoint(
                controller.center - Vector3.up * controller.height * .5f));
            visual.Configure(health, spell, feet);
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
    }

    // адаптируем материалы и ограничиваем частицы; исходные ассеты пакета остаются без изменений.
    private static void PrepareParticles(GameObject root)
    {
        foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.stopAction = ParticleSystemStopAction.None;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = particles.name == "Sparks" ? 64 : 24;
            var lights = particles.lights;
            lights.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterials = renderer.sharedMaterials.Select(ConvertMaterial).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        foreach (Light light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
    }

    // сохраняем уже совместимый материал кристалла; старые частицы переводим в urp с исходными текстурами и смешиванием.
    public static Material ConvertMaterial(Material source)
    {
        string path = MaterialsPath + "/" + source.name + ".mat";
        Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result != null) return result;
        if (source.shader.name.StartsWith("Universal Render Pipeline/")) result = new Material(source);
        else
        {
            result = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            result.SetTexture("_BaseMap", source.mainTexture);
            result.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            bool additive = source.HasProperty("_DstBlend") && source.GetFloat("_DstBlend") == (float)BlendMode.One;
            result.SetFloat("_Surface", 1);
            result.SetFloat("_Blend", additive ? 2 : 0);
            result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            result.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            result.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            result.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            result.SetFloat("_ZWrite", 0);
            result.SetFloat("_Cull", (float)CullMode.Off);
            result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            result.SetOverrideTag("RenderType", "Transparent");
            result.renderQueue = 3000;
        }
        result.name = "Spell " + source.name;
        result.SetShaderPassEnabled("ShadowCaster", false);
        AssetDatabase.CreateAsset(result, path);
        return result;
    }

    // проверяем прежние параметры снаряда, сетевые ссылки и время всех основных слоёв щита.
    [MenuItem("Tools/Wizard War/Validate ice and shield effects")]
    public static void Validate()
    {
        var ice = AssetDatabase.LoadAssetAtPath<ElementalSpell>("Assets/Scripts/Spells/Elemental/IceShard.asset");
        var stone = AssetDatabase.LoadAssetAtPath<ElementalSpell>("Assets/Scripts/Spells/Elemental/StoneSkin.asset");
        if (ice.damage != 22 || ice.speed != 24 || ice.duration != 4 || ice.effectPrefab.GetComponent<NetworkIdentity>() == null)
            throw new InvalidOperationException("Ice gameplay settings changed.");
        var crystal = ice.effectPrefab.GetComponentsInChildren<ParticleSystem>().Single(item => item.name == "Ice spear - Crystal effect blue");
        if (Vector3.Dot(crystal.transform.localRotation * Vector3.up, Vector3.forward) < .99f)
            throw new InvalidOperationException("Crystal must point along the projectile.");
        if (stone.duration != 5 || stone.shieldAmount != 50 || stone.effectPrefab == null)
            throw new InvalidOperationException("Stone skin settings changed.");
        foreach (var particles in stone.effectPrefab.GetComponentsInChildren<ParticleSystem>())
            if (!particles.main.loop || particles.main.duration != stone.duration || particles.lights.enabled)
                throw new InvalidOperationException("Shield layers must follow spell duration without extra lights.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponentInChildren<StoneSkinShieldVisual>(true) == null)
            throw new InvalidOperationException("Player shield visual is missing.");
        Preview(ice.effectPrefab, false);
        Preview(stone.effectPrefab, true);
        File.WriteAllText("Logs/ice-shield-effects-validation.txt", "PASS: horizontal crystal; ice damage/speed/lifetime unchanged; shield duration 5s and capacity 50; client visual connected to synchronized shield.");
    }

    // проверяем вид частиц вне игровой сцены и наличие основных слоёв перед окончанием способности.
    private static void Preview(GameObject prefab, bool shield)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(800, 600, 24);
        var image = new Texture2D(800, 600, TextureFormat.RGB24, false);
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var cameraObject = new GameObject("Effect preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = shield ? new Vector3(3, 2.3f, -4) : new Vector3(2, 1.2f, -1.8f);
            camera.transform.LookAt(shield ? Vector3.up : Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .07f, .085f);
            camera.targetTexture = target;
            var lightObject = new GameObject("Preview light", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            var particles = instance.GetComponentsInChildren<ParticleSystem>();
            foreach (var system in particles) system.Simulate(shield ? 1 : .15f, false, true);
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 800, 600), 0, 0);
            image.Apply();
            File.WriteAllBytes("Logs/IceAndShieldEffects/" + (shield ? "shield" : "ice") + ".png", image.EncodeToPNG());
            foreach (var system in particles)
            {
                system.Simulate(shield ? 4.8f : 3.8f, false, true);
                if ((system.name == "Sphere" || system.name == "Ice spear - Crystal effect blue") && system.particleCount == 0)
                    throw new InvalidOperationException("Main visual expires before spell: " + system.name);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
