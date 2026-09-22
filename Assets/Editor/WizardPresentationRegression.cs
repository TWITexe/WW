using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Интеграция геометрии с реальными компонентами: смещённый FBX-pivot, возврат и привязка щита.
public static class WizardPresentationRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("Wizard presentation: " + message);
        checks++;
    }
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);

    [MenuItem("Tools/Wizard War/Validate staff and shield presentation")]
    public static void Run()
    {
        checks = 0;
        var root = new GameObject("Presentation test");
        root.SetActive(false);
        var effectPrefab = new GameObject("Shield test prefab");
        var spell = ScriptableObject.CreateInstance<ElementalSpell>();
        try
        {
            var health = root.AddComponent<Health>();
            var appearance = root.AddComponent<WizardAppearance>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = Vector3.down;
            visual.localScale = Vector3.one * .91f;
            Transform body = Part(visual, "Body", new Vector3(0, .8f, 0), new Vector3(.7f, 1.5f, .6f));
            Transform head = Part(visual, "Head", new Vector3(0, 1.7f, 0), Vector3.one * .45f);
            Transform hat = Part(visual, "Hat", new Vector3(0, 2.1f, 0), new Vector3(.7f, .4f, .7f));
            Transform staff = Part(visual, "Staff", new Vector3(.9f, 1.1f, .08f), new Vector3(.12f, 2.4f, .12f));
            appearance.visualRoot = visual;
            appearance.pieces = new[] { body, head, hat, staff };
            Call(appearance, "Awake");
            Vector3 grip = Field<Vector3>(appearance, "staffGripLocal");
            var staffMesh = staff.GetComponentInChildren<MeshFilter>(true);
            Check(Vector3.Distance(staff.TransformPoint(grip), staffMesh.transform.TransformPoint(staffMesh.sharedMesh.bounds.center)) < .0001f,
                "grip is actual geometry centre with offset pivot and scaled parent");
            float uprightClearance = (float)Call(appearance, "StaffClearance", Vector3.zero);
            visual.localRotation = Quaternion.Euler(0, 0, 22);
            Check((float)Call(appearance, "StaffClearance", Vector3.zero) > uprightClearance,
                "staff clearance expands around tilted body and hat");
            visual.localRotation = Quaternion.identity;
            Vector3 overhead = new Vector3(.05f, 2, -.1f);
            for (int i = 0; i <= 120; i++)
            {
                Quaternion rotation = Quaternion.AngleAxis(i * 6, Vector3.up) * Quaternion.Euler(0, 0, 90);
                Call(appearance, "ApplyStaffPose", new Pose(overhead, rotation), Vector3.zero, i == 0, 1f / 60);
                Check(Vector3.Distance(staff.TransformPoint(grip), overhead) < .0001f,
                    "staff centre stays above head while ends rotate");
            }
            Pose idle = new Pose(new Vector3(1, .4f, 0), Quaternion.identity);
            Vector3 previous = staff.TransformPoint(grip);
            Call(appearance, "ApplyStaffPose", idle, Vector3.zero, false, 1f / 60);
            Check(Vector3.Distance(staff.TransformPoint(grip), previous) < .08f, "return begins without snap");
            for (int i = 0; i < 120; i++) Call(appearance, "ApplyStaffPose", idle, Vector3.zero, false, 1f / 60);
            Check(Vector3.Distance(staff.TransformPoint(grip), idle.position) < .001f, "return settles at rest");
            Vector3 teleported = new Vector3(100, 0, 0);
            Call(appearance, "ApplyStaffPose", new Pose(teleported + idle.position, Quaternion.identity), teleported, true, 1f / 60);
            Check(Vector3.Distance(staff.TransformPoint(grip), teleported + idle.position) < .001f, "teleport resets spring");

            var shield = root.AddComponent<StoneSkinShieldVisual>();
            spell.effectPrefab = effectPrefab;
            Vector3 shieldLocal = new Vector3(0, -1, 0);
            shield.Configure(health, spell, shieldLocal);
            Call(shield, "Awake");
            Call(shield, "Refresh", 50);
            GameObject effect = Field<GameObject>(shield, "instance");
            visual.localPosition += new Vector3(.2f, .17f, -.1f);
            root.transform.SetPositionAndRotation(new Vector3(4, 2, 3), Quaternion.Euler(0, 105, 0));
            Call(shield, "LateUpdate");
            Vector3 expected = root.transform.TransformPoint(shieldLocal) + appearance.VisualDisplacement;
            Check(Vector3.Distance(effect.transform.position, expected) < .0001f, "shield follows displayed body smoothing and hover");
            Check(Quaternion.Angle(effect.transform.rotation, Quaternion.identity) < .01f, "shield particles do not inherit body yaw");
            staff.localPosition += new Vector3(10, 20, -10);
            hat.localPosition += Vector3.up * .1f;
            Call(shield, "LateUpdate");
            Check(Vector3.Distance(effect.transform.position, expected) < .0001f, "casting staff and hat cannot move shield");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(effectPrefab);
            UnityEngine.Object.DestroyImmediate(spell);
        }
        Debug.Log("WIZARD_PRESENTATION_PASSED: " + checks + " checks");
    }

    private static Transform Part(Transform visual, string name, Vector3 centre, Vector3 size)
    {
        var part = new GameObject(name).transform;
        part.SetParent(visual, false);
        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(mesh.GetComponent<Collider>());
        mesh.transform.SetParent(part, false);
        mesh.transform.localPosition = centre;
        mesh.transform.localScale = size;
        return part;
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
