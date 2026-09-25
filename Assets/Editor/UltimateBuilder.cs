using System;
using System.IO;
using System.Linq;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Installs only the new components and UI entries; existing scene arrangements stay editable.
public static class UltimateBuilder
{
    const string Folder = "Assets/Spells/Ultimates";
    const string CatalogPath = Folder + "/UltimateCatalog.asset";
    static TMP_FontAsset font;
    static Material stone, ice, ember, water, white;
    static readonly string[] Titles = { "Призматический залп", "Рождение Феникса", "Паровой взлёт", "Дух 3 стихий", "Похищение тепла", "Недра земли", "Зеркальный лабиринт", "Полярный пробой", "Переворот притяжения", "Ледниковый таран" };
    static readonly int[][] Elements = { new[]{0,1,2}, new[]{0,1,3}, new[]{0,1,4}, new[]{0,2,3}, new[]{0,2,4}, new[]{0,3,4}, new[]{1,2,3}, new[]{1,2,4}, new[]{1,3,4}, new[]{2,3,4} };
    static readonly float[] Durations = { 10, 0, 15, 10, 7, 12, 12, 10, 6, 8 };
    static readonly string[] Descriptions = {
        "На 10 с создаёт три кристалла. Фаерболлы и ледяные копья сохраняют в них копии с 60% урона. F выпускает накопленный залп. Максимум 3 копии.",
        "Аура до первого смертельного удара. Вместо смерти маг становится метеоритом с 500 HP на 4 с. Если метеорит уцелел, маг возвращается с полным здоровьем.",
        "Управляемый полёт на 15 с с обычными заклинаниями. WASD — движение, Пробел / Ctrl — вверх / вниз. F — приземлиться. Высота до 12 м над точкой старта.",
        "Боевой голем на 10 с, 200 HP. ЛКМ: огненный разлом на 30 урона. ПКМ: мороз на 20 урона и замедление 50% на 2 с. Усиленный прыжок. Гибель голема убивает мага.",
        "Луч на 7 с: 20 урона/с, лечение владельца на 100% нанесённого урона. Союзники в 6 м получают 50%. Укрытия блокируют луч; во время применения обычные заклинания недоступны.",
        "На 12 с поднимает вулканическую конструкцию с укрытиями и потоками лавы: 12 урона/с. F обрушает её, нанося 30 урона в радиусе 6 м.",
        "Шесть ледяных зеркал на 12 с, по 90 прочности. Отражают снаряды обеих сторон с сохранением владельца. Попадания повреждают зеркала; между ними есть проходы.",
        "Три мгновенных выстрела сквозь стены: 80 урона в тело, 120 в голову, без разброса. ЛКМ — выстрел, интервал 0,7 с. Заряды доступны 10 с. Луч останавливается на первом игроке.",
        "Поле радиусом 6 м поднимает всех игроков на 4 м, сохраняя движение и атаки. F: предупреждение 0,8 с, затем обрушение. Удар о землю наносит 40 урона и создаёт hit-эффект.",
        "Управляемая прицелом сфера на 8 с. Таран: 15 урона и толчок, не чаще раза в секунду по цели. F / ЛКМ: взрыв на 40 урона в 4 м и оглушение на 3 с. Во время управления маг неподвижен."
    };

    [MenuItem("Tools/Wizard War/Install ultimates")]
    public static void Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before installing ultimates.");
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/SavedBuildNamesFont.asset");
        if (font == null) font = TMP_Settings.defaultFontAsset;
        stone = Material("Obsidian", new Color(.13f,.16f,.2f), false);
        ice = Material("Ice", new Color(.18f,.65f,.86f), true);
        ember = Material("Magma", new Color(1,.24f,.025f), true);
        water = Material("Steam", new Color(.2f,.82f,.95f), true);
        white = Material("Core", new Color(.85f,.96f,1), true);
        var catalog = AssetDatabase.LoadAssetAtPath<UltimateCatalog>(CatalogPath);
        bool firstInstall = catalog == null;
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<UltimateCatalog>();
            catalog.definitions = new UltimateDefinition[10];
            for (int i = 0; i < 10; i++) catalog.definitions[i] = new UltimateDefinition
            {
                kind = (UltimateKind)i, title = Titles[i], description = Descriptions[i], duration = Durations[i],
                elements = Elements[i].Select(e => (MagicElement)e).ToArray(),
                color = Color.Lerp(new Color(1,.4f,.12f), new Color(.25f,.85f,1), i / 9f)
            };
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        foreach (var kind in new[] { UltimateKind.EarthDepths, UltimateKind.MirrorLabyrinth, UltimateKind.GravityInversion, UltimateKind.GlacierRam })
        {
            var definition = catalog.Get(kind);
            if (definition.worldPrefab == null) definition.worldPrefab = BuildWorld(catalog, kind);
        }
        var phoenix = catalog.Get(UltimateKind.PhoenixBirth);
        phoenix.description = phoenix.description.Replace(" Перезарядка начинается при срабатывании ауры.", "");
        EditorUtility.SetDirty(catalog);
        InstallPlayer(catalog);
        const string hudPath = "Assets/Prefabs/UI/MatchUI.prefab";
        Backup(hudPath);
        var hud = PrefabUtility.LoadPrefabContents(hudPath);
        try { foreach (var ui in hud.GetComponentsInChildren<PlayerGameUI>(true)) InstallHud(ui); PrefabUtility.SaveAsPrefabAsset(hud, hudPath); }
        finally { PrefabUtility.UnloadPrefabContents(hud); }

        Scene original = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/SampleScene.unity" })
        {
            Backup(path);
            Scene scene = SceneManager.GetSceneByPath(path);
            bool loaded = scene.IsValid() && scene.isLoaded;
            if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var manager in root.GetComponentsInChildren<NetManager>(true))
                    {
                        foreach (var definition in catalog.definitions)
                            if (definition.worldPrefab != null && !manager.spawnPrefabs.Contains(definition.worldPrefab)) manager.spawnPrefabs.Add(definition.worldPrefab);
                        EditorUtility.SetDirty(manager);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
                    }
                    foreach (var ui in root.GetComponentsInChildren<PlayerGameUI>(true)) InstallHud(ui);
                    foreach (var shelf in root.GetComponentsInChildren<ShelfSpellCatalogUI>(true)) InstallBook(shelf, catalog);
                }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }
        }
        if (original.IsValid()) SceneManager.SetActiveScene(original);
        AssetDatabase.SaveAssets();
        if (firstInstall) UltimateRevisionBuilder.Run();
        Debug.Log("Installed ten ultimates, player forms, four network effects and central HUD.");
    }

    static Material Material(string name, Color color, bool glow)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", glow ? .7f : .2f);
        if (glow) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.6f); }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
    static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool collision = false)
    {
        var root = GameObject.CreatePrimitive(type); root.name = name; root.transform.SetParent(parent, false);
        root.transform.localPosition = position; root.transform.localScale = scale;
        root.GetComponent<Renderer>().sharedMaterial = material;
        if (collision && type == PrimitiveType.Cylinder)
        {
            Object.DestroyImmediate(root.GetComponent<Collider>());
            var meshCollider = root.AddComponent<MeshCollider>(); meshCollider.sharedMesh = root.GetComponent<MeshFilter>().sharedMesh;
            meshCollider.convex = true;
        }
        if (!collision) { Object.DestroyImmediate(root.GetComponent<Collider>()); root.layer = 2; }
        return root;
    }
    static GameObject Child(Transform parent, string name)
    {
        var root = new GameObject(name); root.transform.SetParent(parent, false); return root;
    }
    static LineRenderer Ring(Transform parent, string name, float radius, Color color, float height = 0)
    {
        var root = Child(parent, name); root.layer = 2;
        var line = root.AddComponent<LineRenderer>(); line.useWorldSpace = false;
        line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/AreaTarget.mat");
        if (line.sharedMaterial == null) line.sharedMaterial = water;
        line.loop = true; line.positionCount = 64; line.widthMultiplier = .055f;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI * 2 / 64;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
        }
        return line;
    }
    static void Sparks(Transform parent, string name, Color color, float radius, float speed, bool downward = false)
    {
        var root = Child(parent, name); root.layer = 2;
        if (downward) root.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var particles = root.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main; main.loop = true; main.startLifetime = .65f; main.startSpeed = speed;
        main.startSize = .11f; main.startColor = color; main.maxParticles = 80; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission; emission.rateOverTime = 32;
        var shape = particles.shape; shape.shapeType = downward ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere;
        shape.radius = radius; shape.angle = 12;
        var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/AreaTarget.mat") ?? water;
    }

    static GameObject BuildWorld(UltimateCatalog catalog, UltimateKind kind)
    {
        var root = new GameObject(kind.ToString(), typeof(NetworkIdentity), typeof(UltimateWorldEffect));
        var effect = root.GetComponent<UltimateWorldEffect>(); effect.catalog = catalog; effect.kind = kind;
        if (kind == UltimateKind.EarthDepths)
        {
            Shape(root.transform, "Raised volcanic core", PrimitiveType.Cylinder, new Vector3(0,.65f,0), new Vector3(3.5f,.65f,3.5f), stone, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f;
                var lava = Shape(root.transform, "Lava channel " + i, PrimitiveType.Cube,
                    new Vector3(Mathf.Sin(a)*3.5f,.055f,Mathf.Cos(a)*3.5f), new Vector3(1.2f,.08f,4.5f), ember);
                lava.transform.localRotation = Quaternion.Euler(0, i * 90, 0);
                var ramp = Shape(root.transform, "Stone approach " + i, PrimitiveType.Cube,
                    new Vector3(Mathf.Sin(a+.6f)*2.8f,.65f,Mathf.Cos(a+.6f)*2.8f), new Vector3(1.3f,.25f,3.4f), stone, true);
                ramp.transform.localRotation = Quaternion.Euler(18, i*90+34, 0);
                var rock = Shape(root.transform, "Obsidian shelter " + i, PrimitiveType.Cube,
                    new Vector3(Mathf.Sin(a+.6f)*4.7f,.95f,Mathf.Cos(a+.6f)*4.7f), new Vector3(1.3f,1.9f,1.2f), stone, true);
                rock.transform.localRotation = Quaternion.Euler(0, i*90+25, 0);
                Shape(rock.transform, "Molten seam", PrimitiveType.Cube, new Vector3(.12f,0,-.51f), new Vector3(.08f,.85f,.035f), ember);
            }
            Ring(root.transform, "Caldera edge", 5.8f, new Color(1,.3f,.05f), .12f);
            Sparks(root.transform, "Embers", new Color(1,.4f,.08f), 2, .6f);
        }
        else if (kind == UltimateKind.MirrorLabyrinth)
        {
            effect.mirrors = new UltimateMirror[6];
            for (int i = 0; i < 6; i++)
            {
                Vector3 position = new Vector3((i%3-1)*3.4f, 1.6f, (i/3*2-1)*2.4f);
                var panel = Shape(root.transform, "Mirror " + (i+1), PrimitiveType.Cube, position, new Vector3(2,3.2f,.14f), ice, true);
                panel.transform.localRotation = Quaternion.Euler(0, (i%2==0 ? 35 : -35), 0);
                var mirror = panel.AddComponent<UltimateMirror>(); mirror.effect = effect; mirror.index = i; effect.mirrors[i] = mirror;
                for (int side = -1; side <= 1; side += 2)
                    Shape(panel.transform, "Crystal frame", PrimitiveType.Cube, new Vector3(side*.5f,0,0), new Vector3(.07f,1.05f,1.4f), white);
            }
        }
        else if (kind == UltimateKind.GravityInversion)
        {
            effect.warningRing = Ring(root.transform, "Gravity boundary", 6, Color.cyan, .1f);
            Ring(root.transform, "Upper boundary", 6, new Color(.4f,.7f,1,.5f), 4);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * .25f;
                var rune = Shape(root.transform, "Lift rune " + i, PrimitiveType.Cube,
                    new Vector3(Mathf.Cos(a)*5.8f,2,Mathf.Sin(a)*5.8f), new Vector3(.18f,.6f,.18f), water);
                rune.transform.localRotation = Quaternion.Euler(0, i*45, 45);
            }
            Sparks(root.transform, "Rising motes", Color.cyan, 5, 1);
        }
        else
        {
            var collider = root.AddComponent<SphereCollider>(); collider.radius = catalog.orbRadius; collider.isTrigger = true;
            var model = Child(root.transform, "Rolling glacier"); effect.rotatingVisual = model.transform;
            Shape(model.transform, "Ice core", PrimitiveType.Sphere, Vector3.zero, Vector3.one*2, ice);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2 / 12;
                var shard = Shape(model.transform, "Frozen rock " + i, PrimitiveType.Cube,
                    new Vector3(Mathf.Cos(a)*.83f, Mathf.Sin(i*2.3f)*.45f, Mathf.Sin(a)*.83f), new Vector3(.42f,.75f,.4f), i%3==0 ? stone : white);
                shard.transform.localRotation = Quaternion.Euler(i*27, i*31, i*13);
            }
        }
        string path = Folder + "/" + kind + ".prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root);
        EnsureIdentity(path);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    static void InstallPlayer(UltimateCatalog catalog)
    {
        const string path = "Assets/Prefabs/Player.prefab";
        Backup(path);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var entity = root.GetComponentInChildren<Health>(true).gameObject;
            var ultimate = entity.GetComponent<PlayerUltimate>() ?? entity.AddComponent<PlayerUltimate>();
            ultimate.catalog = catalog;
            if (ultimate.presentation == null) ultimate.presentation = BuildPresentation(entity.transform);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        EnsureIdentity(path);
    }
    static UltimatePresentation BuildPresentation(Transform parent)
    {
        var root = Child(parent, "Ultimate visuals");
        var view = root.AddComponent<UltimatePresentation>();
        view.phoenixAura = Child(root.transform, "Phoenix aura");
        Ring(view.phoenixAura.transform, "Fire halo", .9f, new Color(1,.5f,.05f), .7f);
        Ring(view.phoenixAura.transform, "Crown", .55f, new Color(1,.8f,.2f), 1.9f);
        Sparks(view.phoenixAura.transform, "Phoenix embers", new Color(1,.55f,.05f), .7f, .3f);
        view.meteor = Child(root.transform, "Phoenix meteor");
        Shape(view.meteor.transform, "Meteor shell", PrimitiveType.Sphere, new Vector3(0,.55f,0), new Vector3(3.1f,3,3), stone);
        var meteorHit = view.meteor.AddComponent<SphereCollider>(); meteorHit.center = new Vector3(0,.55f,0); meteorHit.radius = 1.55f; meteorHit.isTrigger = true;
        view.meteorCollider = meteorHit;
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2 / 12;
            var crack = Shape(view.meteor.transform, "Molten fissure " + i, PrimitiveType.Cube,
                new Vector3(Mathf.Cos(a)*1.46f,.55f+Mathf.Sin(i*1.7f)*.45f,Mathf.Sin(a)*1.46f), new Vector3(.12f,1.25f,.06f), ember);
            crack.transform.localRotation = Quaternion.Euler(0, 90-i*30, (i%3-1)*25);
        }
        Sparks(view.meteor.transform, "Rebirth core", new Color(1,.4f,.02f), 1.5f, .4f);
        var bar = new GameObject("Meteor health", typeof(RectTransform), typeof(Canvas));
        bar.transform.SetParent(root.transform, false); bar.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        view.meteorBar = bar.GetComponent<RectTransform>(); view.meteorBar.sizeDelta = new Vector2(1.9f,.14f); view.meteorBar.localPosition = new Vector3(0,2.5f,0);
        var bg = Image(bar.transform, "Background", new Color(.02f,.02f,.03f,.95f), Vector2.zero, Vector2.one);
        view.meteorFill = Image(bg.transform, "Fill", new Color(1,.55f,.12f), Vector2.zero, Vector2.one);
        view.meteorFill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        view.meteorFill.type = UnityEngine.UI.Image.Type.Filled; view.meteorFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        view.meteorFill.fillOrigin = 0;
        view.spirit = Child(root.transform, "Spirit of three elements");
        Shape(view.spirit.transform, "Stone torso", PrimitiveType.Cube, new Vector3(0,.85f,0), new Vector3(1.25f,1.65f,.9f), stone);
        Shape(view.spirit.transform, "Molten heart", PrimitiveType.Sphere, new Vector3(0,1,-.46f), Vector3.one*.55f, ember);
        Shape(view.spirit.transform, "Crowned head", PrimitiveType.Cube, new Vector3(0,2.05f,0), Vector3.one*.7f, ice);
        for (int side = -1; side <= 1; side += 2)
        {
            Shape(view.spirit.transform, "Ice shoulder", PrimitiveType.Cube, new Vector3(side*.95f,1.35f,0), new Vector3(.7f,.85f,.85f), ice);
            Shape(view.spirit.transform, "Stone arm", PrimitiveType.Cube, new Vector3(side*1.05f,.45f,0), new Vector3(.55f,1.1f,.65f), stone);
            Shape(view.spirit.transform, "Magma fist", PrimitiveType.Sphere, new Vector3(side*1.05f,-.05f,.2f), Vector3.one*.65f, ember);
            Shape(view.spirit.transform, "Stone leg", PrimitiveType.Cube, new Vector3(side*.4f,-.5f,0), new Vector3(.55f,1,.65f), stone);
        }
        var body = view.spirit.AddComponent<BoxCollider>(); body.center = new Vector3(0,.75f,0); body.size = new Vector3(2.6f,3.5f,1); body.isTrigger = true;
        view.spiritColliders = new Collider[]{body};
        view.flight = Child(root.transform, "Steam flight");
        view.flight.transform.localPosition = new Vector3(0,-.65f,0);
        Sparks(view.flight.transform, "Steam thrust", new Color(.55f,.85f,1), .3f, 5, true);
        Sparks(view.flight.transform, "Hot core", new Color(1,.65f,.15f), .12f, 3, true);
        view.prisms = Child(root.transform, "Prismatic crystals"); view.crystals = new Transform[3];
        for (int i = 0; i < 3; i++)
        {
            float a = (i*120+30)*Mathf.Deg2Rad;
            var crystal = Shape(view.prisms.transform, "Stored shot " + i, PrimitiveType.Cube,
                new Vector3(Mathf.Cos(a)*1.1f, 1.8f+Mathf.Sin(a)*.6f, 0), Vector3.one*.2f, i%2==0 ? ice : ember);
            crystal.transform.localRotation = Quaternion.Euler(35,0,45); view.crystals[i] = crystal.transform;
        }
        view.healingRing = Child(root.transform, "Healing reach");
        Ring(view.healingRing.transform, "Allied healing radius", 6, new Color(.35f,1,.6f,.45f), -.9f);
        var beam = Child(root.transform, "Heat siphon beam"); beam.layer = 2; view.beam = beam.AddComponent<LineRenderer>();
        view.beam.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/AreaTarget.mat") ?? water;
        view.beam.positionCount = 2; view.beam.widthMultiplier = .13f; view.beam.useWorldSpace = true;
        view.beam.enabled = false;
        foreach (var item in new[] { view.phoenixAura, view.meteor, view.spirit, view.flight, view.prisms, view.healingRing, bar }) item.SetActive(false);
        return view;
    }

    static void InstallHud(PlayerGameUI ui)
    {
        var data = new SerializedObject(ui);
        if (data.FindProperty("combo").objectReferenceValue is UnityEngine.UI.Text combo)
        {
            Vector2 position = combo.rectTransform.anchoredPosition;
            position.y = Mathf.Max(position.y, 130);
            combo.rectTransform.anchoredPosition = position;
        }
        var existing = data.FindProperty("ultimateHUD").objectReferenceValue as UltimateHUD;
        if (existing != null)
        {
            if (existing.icon != null && existing.icon.GetComponent<CanvasRenderer>() == null) existing.icon.gameObject.AddComponent<CanvasRenderer>();
            InstallDebugChargeButton(ui, existing);
            return;
        }
        var row = ui.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Cooldown bar");
        var root = new GameObject("Ultimate", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UltimateHUD));
        root.transform.SetParent(row, false); root.GetComponent<RectTransform>().sizeDelta = new Vector2(80,82);
        root.GetComponent<UnityEngine.UI.Image>().color = new Color(.055f,.065f,.09f,.97f);
        root.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        var hud = root.GetComponent<UltimateHUD>();
        hud.frame = Image(root.transform, "Accent", new Color(1,.65f,.25f), new Vector2(0,0), new Vector2(1,.035f));
        var sigil = new GameObject("Sigil", typeof(RectTransform), typeof(UltimateIconGraphic)); sigil.transform.SetParent(root.transform, false);
        var rect = sigil.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.2f,.32f); rect.anchorMax = new Vector2(.8f,.95f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        hud.icon = sigil.GetComponent<UltimateIconGraphic>(); hud.icon.raycastTarget = false; hud.icon.color = new Color(1,.65f,.25f);
        hud.fill = Image(root.transform, "Cooldown", new Color(0,0,0,.75f), Vector2.zero, Vector2.one);
        hud.fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        hud.fill.type = UnityEngine.UI.Image.Type.Filled; hud.fill.fillMethod = UnityEngine.UI.Image.FillMethod.Vertical; hud.fill.fillOrigin = 0; hud.fill.fillAmount = 0;
        hud.timer = Label(root.transform, "Timer", "", 30, new Vector2(0,.2f), Vector2.one);
        hud.keyLabel = Label(root.transform, "Key", "F", 16, Vector2.zero, new Vector2(1,.25f));
        hud.title = Label(root.transform, "Name", "Призматический залп", 11, new Vector2(-.65f,1.04f), new Vector2(1.65f,1.4f));
        hud.title.enableWordWrapping = false;
        data.FindProperty("ultimateHUD").objectReferenceValue = hud; data.ApplyModifiedPropertiesWithoutUndo();
        InstallDebugChargeButton(ui, hud);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
    }
    static void InstallDebugChargeButton(PlayerGameUI ui, UltimateHUD hud)
    {
        if (hud.debugChargeButton != null) return;
        var pause = (GameObject)new SerializedObject(ui).FindProperty("pause").objectReferenceValue;
        var root = new GameObject("Temporary ultimate charge", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        root.transform.SetParent(pause.transform, false);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-24, 24); rect.sizeDelta = new Vector2(320, 48);
        var background = root.GetComponent<UnityEngine.UI.Image>(); background.color = new Color(.32f, .17f, .06f, .98f);
        var button = root.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = background;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var label = Label(root.transform, "Label", "ТЕСТ: Зарядить ульту · F8", 19, Vector2.zero, Vector2.one);
        label.color = new Color(1, .85f, .55f); label.enableWordWrapping = false;
        hud.debugChargeButton = button;
        root.SetActive(false);
        EditorUtility.SetDirty(hud);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
    }
    static void InstallBook(ShelfSpellCatalogUI shelf, UltimateCatalog catalog)
    {
        var data = new SerializedObject(shelf);
        if (data.FindProperty("ultimateEntry").objectReferenceValue is UltimateBookEntry current)
        {
            if (current.icon != null && current.icon.GetComponent<CanvasRenderer>() == null) current.icon.gameObject.AddComponent<CanvasRenderer>();
            return;
        }
        var scroll = (UnityEngine.UI.ScrollRect)data.FindProperty("scroll").objectReferenceValue;
        var root = new GameObject("Ultimate description", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.LayoutElement), typeof(UltimateBookEntry));
        root.transform.SetParent(scroll.content, false); root.transform.SetAsFirstSibling();
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(600,200);
        root.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 210;
        root.GetComponent<UnityEngine.UI.Image>().color = new Color(.075f,.09f,.12f,.95f);
        root.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        var entry = root.GetComponent<UltimateBookEntry>(); entry.catalog = catalog;
        entry.title = Label(root.transform, "Ultimate title", Titles[0] + " · УЛЬТИМЕЙТ", 25, new Vector2(.14f,.76f), new Vector2(.98f,.96f));
        entry.title.alignment = TextAlignmentOptions.Left;
        entry.description = Label(root.transform, "Ultimate details", Descriptions[0], 22, new Vector2(.04f,.06f), new Vector2(.96f,.73f));
        entry.description.alignment = TextAlignmentOptions.TopLeft;
        entry.description.enableAutoSizing = true; entry.description.fontSizeMin = 16; entry.description.fontSizeMax = 22;
        var sigil = new GameObject("Ultimate sigil", typeof(RectTransform), typeof(UltimateIconGraphic)); sigil.transform.SetParent(root.transform, false);
        var rect = sigil.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.03f,.76f); rect.anchorMax = new Vector2(.12f,.96f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        entry.icon = sigil.GetComponent<UltimateIconGraphic>(); entry.icon.raycastTarget = false;
        entry.Refresh(ElementLoadout.Default);
        data.FindProperty("ultimateEntry").objectReferenceValue = entry; data.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(shelf);
    }
    static UnityEngine.UI.Image Image(Transform parent, string name, Color color, Vector2 min, Vector2 max)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image)); root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = root.GetComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return image;
    }
    static TMP_Text Label(Transform parent, string name, string text, float size, Vector2 min, Vector2 max)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var label = root.GetComponent<TextMeshProUGUI>(); label.font = font; label.text = text; label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center; label.color = new Color(.96f,.94f,.87f); label.raycastTarget = false;
        return label;
    }
    static void EnsureIdentity(string path)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path); var identity = root.GetComponent<NetworkIdentity>();
        if (identity == null) return;
        uint id = identity.assetId; EditorUtility.SetDirty(identity); PrefabUtility.SavePrefabAsset(root);
    }
    static void Backup(string path)
    {
        if (!File.Exists(path)) return;
        string target = "Logs/UltimatesBefore/" + path;
        if (File.Exists(target)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(path, target);
    }
}
