using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// точечная правка префабов и проверки исправлений после игрового теста.
public static class SpellFollowupEditor
{
    const string Folder = "Logs/SpellFollowup/";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Apply()
    {
        if (Application.isPlaying) throw new Exception("для сохранения префабов нужен режим редактирования");
        Directory.CreateDirectory(Folder);
        var changes = new List<string>();
        var catalog = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<SpellManager>().Spells;
        foreach (var spell in catalog)
        {
            GameObject prefab = spell is FireBall fire ? fire.PreviewPrefab : spell is WindFlow wind ? wind.PreviewPrefab :
                spell is ElementalSpell elemental && elemental.mode == ElementalCastMode.Bolt ? elemental.effectPrefab : null;
            if (prefab == null || !PlayerNetworkCaster.LargeProjectile(prefab)) continue;
            string path = AssetDatabase.GetAssetPath(spell);
            string backup = Backup(path);
            // читаем исходную скорость из копии: повтор команды не замедляет снаряд ещё раз.
            var match = Regex.Match(File.ReadAllText(backup), @"\n  speed: ([0-9.]+)");
            if (!match.Success) throw new Exception("не найдена исходная скорость " + path);
            float before = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var data = new SerializedObject(spell);
            data.FindProperty("speed").floatValue = before * .7f;
            data.ApplyModifiedPropertiesWithoutUndo();
            changes.Add(spell.Name + ": " + before + " → " + before * .7f + " м/с");
        }
        AssetDatabase.SaveAssets();
        const string mirrorPath = "Assets/TacticalSpells/IceMirror.prefab";
        Backup(mirrorPath);
        var root = PrefabUtility.LoadPrefabContents(mirrorPath);
        try
        {
            var follow = root.GetComponent<IceMirrorVisual>();
            if (follow == null) follow = root.AddComponent<IceMirrorVisual>();
            if (follow.graphics == null)
            {
                var children = root.transform.Cast<Transform>().ToArray();
                var graphics = new GameObject("Mirror graphics").transform;
                graphics.SetParent(root.transform, false);
                foreach (var child in children)
                {
                    if (child.GetComponentInChildren<Collider>(true) != null)
                        throw new Exception("физику зеркала нельзя переносить вместе с графикой");
                    child.SetParent(graphics, false);
                }
                follow.graphics = graphics;
                foreach (var child in graphics.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
                foreach (var particles in graphics.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particles.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, mirrorPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        File.WriteAllLines(Folder + "changes.txt", changes);
        Check();
    }

    static string Backup(string path)
    {
        string copy = Folder + "Before/" + path;
        if (!File.Exists(copy))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            File.Copy(path, copy);
        }
        return copy;
    }

    public static void Check()
    {
        if (Application.isPlaying) throw new Exception("проверка выполняется вне матча");
        WizardAnimationRegression.Run();
        WizardPresentationRegression.Run();
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            CheckLaunch();
            CheckStaffAndMirror();
            CheckTrapVisibility();
            File.WriteAllText(Folder + "validation.txt",
                "PASS: кривые анимации и плавный возврат; после каста сохраняется инерция посоха; зеркало следует за отображаемым телом независимо от сетевого корня; крупный снаряд выпускается перед лицом, направлен к прицелу, не пересекает пол и не появляется за стеной; взведённая печать слабо видна владельцу и скрыта от противника.\n" + DateTime.Now.ToString("O"));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
        }
    }

    static void CheckLaunch()
    {
        Vector3 origin = new Vector3(24000, 24000, 24000);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = origin - Vector3.up * .25f;
        floor.transform.localScale = new Vector3(30, .5f, 30);
        var player = new GameObject("проверка выпуска", typeof(Mirror.NetworkIdentity), typeof(PlayerNetworkCaster));
        player.transform.position = origin + Vector3.up;
        var muzzle = new GameObject("точка перед лицом").transform;
        muzzle.SetParent(player.transform); muzzle.position = origin + new Vector3(0, 1.638f, 1.388f);
        var caster = player.GetComponent<PlayerNetworkCaster>();
        typeof(Mirror.NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Private).Invoke(player.GetComponent<Mirror.NetworkIdentity>(), null);
        Set(caster, "firePoint", muzzle);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FireBall.prefab");
        var ray = new Ray(origin + new Vector3(2, 2, -4), Vector3.forward);
        Physics.SyncTransforms();
        object[] args = { prefab, ray, Vector3.forward, Vector3.zero, Vector3.zero };
        Require((bool)Call(caster, "TryProjectileLaunch", args), "нет свободной точки над полом");
        var point = (Vector3)args[3];
        Require(Vector3.Distance(point, muzzle.position) < .001f, "снаряд не вылетает из точки перед лицом");
        Require(Vector3.Cross(point - ray.origin, ray.direction).sqrMagnitude > 1,
            "выпуск ошибочно перенесён на луч камеры");
        Require(Vector3.Dot((Vector3)args[4], (ray.GetPoint(100) - point).normalized) > .999f,
            "снаряд не направлен к точке прицела");

        // при низкой точке выпуска сохраняем положение перед магом и освобождаем коллайдер от пола.
        muzzle.position = origin + new Vector3(0, .2f, .5f);
        Physics.SyncTransforms();
        Require((bool)Call(caster, "TryProjectileLaunch", args), "нет свободной точки над полом");
        point = (Vector3)args[3];
        Require(point.y - origin.y > PlayerNetworkCaster.ProjectileHalfSize(prefab).y,
            "ядро появляется внутри пола");
        Require(Vector3.ProjectOnPlane(point - muzzle.position, Vector3.up).sqrMagnitude < .0001f,
            "защита от пола сдвигает снаряд в сторону камеры");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = origin + new Vector3(0, 2, .25f);
        wall.transform.localScale = new Vector3(10, 5, .1f);
        Physics.SyncTransforms();
        bool spawned = (bool)Call(caster, "TryProjectileLaunch", args);
        Require(!spawned, "снаряд появляется за стеной перед магом");
    }

    static void CheckStaffAndMirror()
    {
        var root = new GameObject("проверка графики", typeof(Mirror.NetworkIdentity), typeof(WizardAppearance));
        root.transform.position = new Vector3(26000, 26000, 26000);
        var appearance = root.GetComponent<WizardAppearance>();
        var body = new GameObject("Visual").transform; body.SetParent(root.transform, false);
        appearance.visualRoot = body;
        var staff = new GameObject("Staff").transform; staff.SetParent(body, false);
        Set(appearance, "staff", staff);
        Set(appearance, "staffAvoidBody", true);
        Set(appearance, "castGesture", WizardCastGesture.None);
        Set(appearance, "displayedStaffRotation", Quaternion.identity);
        Pose idle = new Pose(root.transform.position + new Vector3(.25f, .4f, -.2f), Quaternion.identity);
        for (int i = 0; i < 180; i++) Call(appearance, "ApplyStaffPose", idle, root.transform.position, false, 1f / 60);
        Require(!(bool)Get(appearance, "staffAvoidBody") && Vector3.Distance(staff.position, idle.position) < .001f,
            "остаточная защита после каста подавляет отставание посоха");
        var mirror = new GameObject("проверка зеркала", typeof(IceMirrorVisual));
        var graphics = new GameObject("graphics").transform; graphics.SetParent(mirror.transform, false);
        var follow = mirror.GetComponent<IceMirrorVisual>(); follow.graphics = graphics;
        Set(follow, "ownerAppearance", appearance);
        body.localPosition = new Vector3(.12f, .17f, -.1f);
        Call(follow, "FollowOwner");
        Vector3 expected = root.transform.position + root.transform.forward * 1.5f + appearance.VisualDisplacement;
        Require(Vector3.Distance(graphics.position, expected) < .001f, "зеркало не следует за сглаженной моделью");
        mirror.transform.position += new Vector3(1, 0, -1);
        Call(follow, "FollowOwner");
        Require(Vector3.Distance(graphics.position, expected) < .001f, "сетевой скачок зеркала сдвигает его графику");
    }

    // проверяем реальную клиентскую ветку видимости, возвращая прежнего локального игрока после проверки.
    static void CheckTrapVisibility()
    {
        var playerProperty = typeof(Mirror.NetworkClient).GetProperty("localPlayer", BindingFlags.Static | BindingFlags.Public);
        var previousPlayer = Mirror.NetworkClient.localPlayer;
        var viewer = new GameObject("владелец печати", typeof(Mirror.NetworkIdentity)).GetComponent<Mirror.NetworkIdentity>();
        typeof(Mirror.NetworkIdentity).GetProperty("netId").SetValue(viewer, 990001u);
        var root = new GameObject("проверка печати", typeof(Mirror.NetworkIdentity), typeof(TacticalEffect), typeof(FireSealCircleVisual));
        var effect = root.GetComponent<TacticalEffect>();
        var visual = root.GetComponent<FireSealCircleVisual>();
        var graphics = GameObject.CreatePrimitive(PrimitiveType.Quad);
        graphics.transform.SetParent(root.transform, false);
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", Color.white);
        var renderer = graphics.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        visual.Configure(effect, graphics, new[] { renderer });
        effect.ownerId = viewer.netId;
        Set(effect, "bornAt", Mirror.NetworkTime.time - 2);
        try
        {
            playerProperty.SetValue(null, viewer);
            Call(visual, "Start");
            Call(visual, "LateUpdate");
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            float opacity = properties.GetColor("_BaseColor").a;
            Require(graphics.activeSelf && opacity > 0 && opacity < .15f, "владелец не видит слабый след печати");
            effect.ownerId = 990002;
            Call(visual, "LateUpdate");
            Require(!graphics.activeSelf, "противник видит взведённую печать");
            Set(effect, "bornAt", Mirror.NetworkTime.time);
            Call(visual, "LateUpdate");
            Require(graphics.activeSelf, "предупреждение до взведения скрыто");
        }
        finally
        {
            playerProperty.SetValue(null, previousPlayer);
            Object.DestroyImmediate(material);
        }
    }

    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
}
