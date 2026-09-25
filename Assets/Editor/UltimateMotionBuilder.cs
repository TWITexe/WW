using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Updates the accepted model's clips without rebuilding any of its geometry or materials.
public static class UltimateMotionBuilder
{
    const string Folder = "Assets/Spells/Ultimates";
    [MenuItem("Tools/Wizard War/Update ultimate movement and durations")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating animations.");
        const string path = Folder + "/ElementalSpiritModel.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { ConfigureRig(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        ApplyDurations(AssetDatabase.LoadAssetAtPath<UltimateCatalog>(Folder + "/UltimateCatalog.asset"));
        AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/ultimate-motion-assets.txt", "PASS: 27-second mirrors/island, 11-second ram; saved Hover, Walk, SwingLeft and SwingRight on the existing golem.");
    }
    public static void ApplyDurations(UltimateCatalog catalog)
    {
        catalog.Get(UltimateKind.MirrorLabyrinth).duration = 27;
        catalog.Get(UltimateKind.MirrorLabyrinth).description = "Шесть зеркал по 90 HP: ЛКМ — поставить, колесо — повернуть, ПКМ — убрать прицел, F — продолжить. 45 с на размещение, после шестого — 27 с действия. Зеркала отражают снаряды.";
        catalog.Get(UltimateKind.EarthDepths).duration = 27;
        catalog.Get(UltimateKind.EarthDepths).description = "Под магом поднимается остров на высоту 5 м. Жар на поверхности: 12 урона/с. F или конец 27 с: остров падает и при ударе взрывается на 30 урона в радиусе 6 м.";
        catalog.Get(UltimateKind.GlacierRam).duration = 11;
        catalog.Get(UltimateKind.GlacierRam).description = "Сфера на 11 с с камерой от третьего лица. WASD — движение, Пробел — прыжок, мышь — камера. Маг стоит на месте. Таран: 15 урона раз в секунду. F / ЛКМ: взрыв на 40 урона в 4 м и оглушение на 3 с.";
        catalog.Get(UltimateKind.ElementalSpirit).description = "Голем на 10 с, 200 HP. WASD — ходьба, Пробел — прыжок. ЛКМ: размашистый удар слева направо и огненный разлом на 30 урона. ПКМ: удар справа налево и мороз на 20 урона, замедляющий на 50% на 2 с. Гибель голема убивает мага.";
        catalog.orbJumpSpeed = 8;
        EditorUtility.SetDirty(catalog);
    }
    public static void ConfigureRig(GameObject root)
    {
        var animation = root.GetComponent<Animation>();
        foreach (var name in animation.Cast<AnimationState>().Select(s => s.name).ToArray()) animation.RemoveClip(name);
        foreach (var clip in new[] { Idle(), Walk(), Swing(false), Swing(true) }) animation.AddClip(clip, clip.name);
        animation.clip = animation.GetClip("Hover"); animation.playAutomatically = true;
        var actor = root.GetComponent<UltimateSpiritAnimator>(); actor.flightAnimation = animation;
    }
    static AnimationClip Clip(string name, float duration, bool loop)
    {
        string path = Folder + "/Spirit " + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves(); clip.name = name; clip.legacy = true; clip.frameRate = 60;
        clip.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
        // Each clip restores all animated channels, so a swing never leaves an arm twisted in idle.
        Pose(clip, "", Vector3.zero, duration);
        Pose(clip, "/FrostArm", new Vector3(-.87f, 1.13f, 0), duration);
        Pose(clip, "/MagmaArm", new Vector3(.87f, 1.13f, 0), duration);
        Pose(clip, "/LeftLeg", new Vector3(-.32f, -.39f, 0), duration);
        Pose(clip, "/RightLeg", new Vector3(.32f, -.39f, 0), duration);
        EditorUtility.SetDirty(clip); return clip;
    }
    static void Pose(AnimationClip clip, string bone, Vector3 position, float duration)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            string suffix = axis == 0 ? "x" : axis == 1 ? "y" : "z";
            clip.SetCurve("Lean/Rig" + bone, typeof(Transform), "localPosition." + suffix, AnimationCurve.Constant(0, duration, position[axis]));
            clip.SetCurve("Lean/Rig" + bone, typeof(Transform), "localEulerAnglesRaw." + suffix, AnimationCurve.Constant(0, duration, 0));
        }
    }
    static void Curve(AnimationClip clip, string bone, string property, float duration, params float[] values)
    {
        var keys = values.Select((v, i) => new Keyframe(duration * i / (values.Length - 1), v)).ToArray();
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0);
        clip.SetCurve("Lean/Rig" + bone, typeof(Transform), property, curve);
    }
    static AnimationClip Idle()
    {
        var clip = Clip("Hover", 2.4f, true);
        Curve(clip, "", "localPosition.y", 2.4f, .03f, .16f, .03f);
        Curve(clip, "/FrostArm", "localEulerAnglesRaw.z", 2.4f, 7, 12, 7);
        Curve(clip, "/MagmaArm", "localEulerAnglesRaw.z", 2.4f, -7, -12, -7);
        Curve(clip, "/FrostArm", "localEulerAnglesRaw.x", 2.4f, -3, 4, -3);
        Curve(clip, "/MagmaArm", "localEulerAnglesRaw.x", 2.4f, 3, -4, 3);
        return clip;
    }
    static AnimationClip Walk()
    {
        const float duration = .9f;
        var clip = Clip("Walk", duration, true);
        Curve(clip, "", "localPosition.y", duration, -.16f, -.08f, -.16f, -.08f, -.16f);
        Curve(clip, "", "localEulerAnglesRaw.y", duration, -5, 0, 5, 0, -5);
        Curve(clip, "/LeftLeg", "localEulerAnglesRaw.x", duration, -32, 0, 32, 0, -32);
        Curve(clip, "/RightLeg", "localEulerAnglesRaw.x", duration, 32, 0, -32, 0, 32);
        Curve(clip, "/LeftLeg", "localPosition.y", duration, -.39f, -.25f, -.39f, -.39f, -.39f);
        Curve(clip, "/RightLeg", "localPosition.y", duration, -.39f, -.39f, -.39f, -.25f, -.39f);
        Curve(clip, "/FrostArm", "localEulerAnglesRaw.x", duration, 24, 0, -24, 0, 24);
        Curve(clip, "/MagmaArm", "localEulerAnglesRaw.x", duration, -24, 0, 24, 0, -24);
        Curve(clip, "/FrostArm", "localEulerAnglesRaw.z", duration, 8, 10, 8, 10, 8);
        Curve(clip, "/MagmaArm", "localEulerAnglesRaw.z", duration, -8, -10, -8, -10, -8);
        return clip;
    }
    static AnimationClip Swing(bool rightToLeft)
    {
        const float duration = .8f;
        var clip = Clip(rightToLeft ? "SwingRight" : "SwingLeft", duration, false);
        string arm = rightToLeft ? "/MagmaArm" : "/FrostArm";
        float side = rightToLeft ? 1 : -1;
        Curve(clip, "", "localPosition.y", duration, .03f, -.05f, -.1f, -.04f, .03f);
        Curve(clip, "", "localEulerAnglesRaw.y", duration, 0, side * 25, -side * 12, -side * 35, 0);
        Curve(clip, "", "localEulerAnglesRaw.x", duration, 0, -8, 10, 6, 0);
        Curve(clip, arm, "localEulerAnglesRaw.x", duration, 0, -80, -85, -70, 0);
        Curve(clip, arm, "localEulerAnglesRaw.y", duration, 0, side * 80, -side * 25, -side * 80, 0);
        Curve(clip, arm, "localPosition.x", duration, side * .87f, side * .95f, side * .55f, side * .3f, side * .87f);
        Curve(clip, arm, "localPosition.z", duration, 0, .08f, .35f, .25f, 0);
        string other = rightToLeft ? "/FrostArm" : "/MagmaArm";
        Curve(clip, other, "localEulerAnglesRaw.x", duration, 0, -25, -35, -20, 0);
        Curve(clip, other, "localEulerAnglesRaw.z", duration, 0, -side * 15, -side * 20, -side * 10, 0);
        return clip;
    }
}
