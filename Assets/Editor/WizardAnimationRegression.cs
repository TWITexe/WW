using System;
using UnityEditor;
using UnityEngine;

// Проверяет реальные кривые, границы переходов, варианты замахов и ограничение отставания шляпы.
public static class WizardAnimationRegression
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Wizard animation: " + message);
        checks++;
    }

    [MenuItem("Tools/Wizard War/Validate cast animations")]
    public static void Run()
    {
        checks = 0;
        Vector3 rest = new Vector3(1.4f, .35f, 0);
        Quaternion restRotation = Quaternion.Euler(4, 15, 3);
        foreach (WizardCastGesture gesture in Enum.GetValues(typeof(WizardCastGesture)))
        {
            if (gesture == WizardCastGesture.None) continue;
            Check(WizardCastMotion.Duration(gesture) > 0, gesture + " duration");
            foreach (Vector3 aim in new[] { Vector3.forward, new Vector3(0, -.6f, 1), Vector3.up, Vector3.down })
                for (byte variant = 0; variant < 6; variant++)
                {
                    Pose first = WizardCastMotion.Sample(gesture, 0, variant, rest, restRotation, aim);
                    Pose last = WizardCastMotion.Sample(gesture, 1, variant, rest, restRotation, aim);
                    Check((first.position - rest).sqrMagnitude < .000001f && (last.position - rest).sqrMagnitude < .000001f,
                        gesture + " returns to rest position");
                    Check(Quaternion.Angle(first.rotation, restRotation) < .01f && Quaternion.Angle(last.rotation, restRotation) < .01f,
                        gesture + " returns to rest rotation");
                    Pose previous = first;
                    for (int step = 1; step <= 500; step++)
                    {
                        Pose pose = WizardCastMotion.Sample(gesture, step / 500f, variant, rest, restRotation, aim);
                        Check(float.IsFinite(pose.position.sqrMagnitude) && float.IsFinite(pose.rotation.x), gesture + " finite pose");
                        Check(Vector3.Distance(previous.position, pose.position) < .15f, gesture + " no position jump");
                        Check(Quaternion.Angle(previous.rotation, pose.rotation) < 15, gesture + " no rotation jump");
                        if (gesture == WizardCastGesture.Shot || gesture == WizardCastGesture.Lob)
                        {
                            Pose safe = WizardCastMotion.KeepOutsideBody(pose, 1.2f, .7f);
                            Vector3 a = Vector3.ProjectOnPlane(safe.position - safe.rotation * Vector3.up * 1.2f, Vector3.up);
                            Vector3 b = Vector3.ProjectOnPlane(safe.position + safe.rotation * Vector3.up * 1.2f, Vector3.up);
                            Vector3 ab = b - a;
                            float along = ab.sqrMagnitude > .000001f ? Mathf.Clamp01(-Vector3.Dot(a, ab) / ab.sqrMagnitude) : 0;
                            Check((a + ab * along).magnitude >= .699f, "whole staff clears body throughout shot and return");
                        }
                        previous = pose;
                    }
                }
        }
        for (byte variant = 0; variant < 6; variant++)
        {
            foreach (var gesture in new[] { WizardCastGesture.Shot, WizardCastGesture.Lob })
            {
                Vector3 aim = new Vector3(.2f, -.3f, 1).normalized;
                Pose release = WizardCastMotion.Sample(gesture, .53f, variant, rest, restRotation, aim);
                Check(Vector3.Dot(release.rotation * Vector3.up, aim) > .999f, gesture + " aims tip at target");
            }
            Pose above = WizardCastMotion.Sample(WizardCastGesture.Lob, .26f, variant, rest, Quaternion.identity, Vector3.forward);
            Check(above.position.y > 1, "lob starts above");
        }
        Pose shotA = WizardCastMotion.Sample(WizardCastGesture.Shot, .26f, 0, rest, restRotation, Vector3.forward);
        Pose shotB = WizardCastMotion.Sample(WizardCastGesture.Shot, .26f, 3, rest, restRotation, Vector3.forward);
        Check(Vector3.Distance(shotA.position, shotB.position) > 1, "different shot variants");
        float minimumWave = float.MaxValue, maximumWave = float.MinValue;
        for (int i = 20; i <= 65; i++)
        {
            Pose orbit = WizardCastMotion.Sample(WizardCastGesture.Self, i / 100f, 0, rest, Quaternion.identity, Vector3.forward);
            Check(Vector3.Dot(orbit.rotation * Vector3.up, Vector3.up) > .96f, "self orbit has gentle tilt");
            minimumWave = Mathf.Min(minimumWave, orbit.position.y);
            maximumWave = Mathf.Max(maximumWave, orbit.position.y);
        }
        Check(maximumWave - minimumWave > .25f, "self orbit undulates vertically");
        Vector3 headAnchor = new Vector3(.17f, 1.93f, -.08f);
        for (int i = 20; i < 48; i++)
        {
            Pose spin = WizardCastMotion.Sample(WizardCastGesture.DelayedStrike, i / 100f, 0, rest,
                restRotation, Vector3.forward, headAnchor);
            Check(Vector3.Distance(spin.position, headAnchor) < .0001f, "meteor rotates about fixed head centre");
        }
        Check(WizardCastMotion.FollowHatHeight(0, 1, .016f, false, 28, .025f, .06f) < 1, "hat trails ascent");
        Check(WizardCastMotion.FollowHatHeight(1, 0, .016f, false, 28, .025f, .06f) > 0, "hat trails descent");
        Check(Mathf.Abs(WizardCastMotion.FollowHatHeight(-100, 1, .016f, false, 28, .025f, .06f) - .975f) < .001f, "rise lag bounded");
        Check(Mathf.Abs(WizardCastMotion.FollowHatHeight(100, 1, .016f, false, 28, .025f, .06f) - 1.06f) < .001f, "fall lag bounded");
        Check(WizardCastMotion.FollowHatHeight(-100, 1, .016f, true, 28, .025f, .06f) == 1, "teleport resets hat");
        float height = 1.06f;
        for (int i = 0; i < 120; i++) height = WizardCastMotion.FollowHatHeight(height, 1, 1f / 60, false, 28, .025f, .06f);
        Check(Mathf.Abs(height - 1) < .00001f, "hat settles on landing");

        var elemental = ScriptableObject.CreateInstance<ElementalSpell>();
        var tactical = ScriptableObject.CreateInstance<TacticalSpell>();
        var advanced = ScriptableObject.CreateInstance<AdvancedSpell>();
        try
        {
            foreach (ElementalCastMode mode in Enum.GetValues(typeof(ElementalCastMode)))
            {
                elemental.mode = mode;
                var expected = mode == ElementalCastMode.Bolt ? WizardCastGesture.Shot :
                    mode == ElementalCastMode.GroundZone ? WizardCastGesture.Area :
                    mode == ElementalCastMode.Tornado ? WizardCastGesture.Vortex : WizardCastGesture.Self;
                Check(WizardCastMotion.ForSpell(elemental) == expected, "elemental mapping " + mode);
            }
            foreach (TacticalKind kind in Enum.GetValues(typeof(TacticalKind)))
            {
                tactical.kind = kind;
                var expected = kind == TacticalKind.SteamDash ? WizardCastGesture.None :
                    kind == TacticalKind.GravityWell ? WizardCastGesture.Area : WizardCastGesture.Construct;
                Check(WizardCastMotion.ForSpell(tactical) == expected, "tactical mapping " + kind);
            }
            foreach (AdvancedSpellKind kind in Enum.GetValues(typeof(AdvancedSpellKind)))
            {
                advanced.kind = kind;
                Check(WizardCastMotion.ForSpell(advanced) != WizardCastGesture.None, "advanced mapping " + kind);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(elemental);
            UnityEngine.Object.DestroyImmediate(tactical);
            UnityEngine.Object.DestroyImmediate(advanced);
        }
        Debug.Log("WIZARD_ANIMATION_PASSED: " + checks + " checks");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
