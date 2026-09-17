using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

public static class SpellVfxInstaller
{
    const string Area = "Assets/TwoUncleVFX/_Effect/_Prefab/_AOE/ChiSpiral.prefab";
    static readonly List<string> changed = new List<string>();
    [MenuItem("Wizard/VFX/Install imported spell effects")]
    public static void Apply()
    {
        changed.Clear();
        Install("Assets/Prefabs/FireBall.prefab", "01", new Color(1, .25f, .035f), .65f, 1, false);
        Install("Assets/Prefabs/WindFlow.prefab", "03", new Color(.55f, 1, .85f), .75f, 1, false);
        foreach (string guid in AssetDatabase.FindAssets("t:ElementalSpell", new[] { "Assets/GeneratedWizard" }))
        {
            var spell = AssetDatabase.LoadAssetAtPath<ElementalSpell>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell.effectPrefab == null) continue;
            bool area = spell.mode != ElementalCastMode.Bolt;
            string variant = spell.name == "Boulder" ? "04" : spell.name == "IceShard" ? "02" : "01";
            Install(AssetDatabase.GetAssetPath(spell.effectPrefab), variant, spell.tint, spell.name == "Boulder" ? .9f : .6f, spell.radius, area);
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var impact = new GameObject("SpellVfxImpact");
        try
        {
            var visual = AddVisual(impact, "01", Color.white, 1, 1, true);
            visual.impact = true;
            const string path = "Assets/Resources/SpellVfxImpact.prefab";
            PrefabUtility.SaveAsPrefabAsset(impact, path); changed.Add(path);
        }
        finally { UnityEngine.Object.DestroyImmediate(impact); }
        AssetDatabase.SaveAssets();
        int checks = 0;
        foreach (string path in changed)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var visual = prefab.GetComponent<SpellVfx>();
            if (visual == null || visual.effect == null || visual.effect.visualEffectAsset == null || !visual.effect.gameObject.activeSelf)
                throw new Exception("Missing VFX: " + path);
            checks++;
            if (visual.area ? !visual.effect.HasFloat("Radius") : !visual.effect.HasGradient("FIreballColorGradient"))
                throw new Exception("VFX parameters did not compile: " + path);
            checks++;
            if (!visual.impact && (prefab.GetComponent<NetworkIdentity>() == null || prefab.GetComponent<Collider>() == null || prefab.GetComponent<Rigidbody>() == null))
                throw new Exception("Network gameplay components missing: " + path);
            checks++;
        }
        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        try
        {
            player.GetComponentInChildren<WizardAppearance>(true).Tint(Color.cyan);
            bool crystal = false;
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    if (renderer.sharedMaterials[i] != null && renderer.sharedMaterials[i].name.Contains("white crystal"))
                    {
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, i);
                        if (block.GetColor("_BaseColor") != Color.cyan) throw new Exception("Staff tip tint failed");
                        crystal = true; checks++;
                    }
            if (!crystal) throw new Exception("Staff crystal material not found");
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/spell-vfx-files.txt", changed);
        Debug.Log("SPELL_VFX_INSTALL_PASSED: " + checks + " checks; " + changed.Count + " prefabs");
    }
    static void Install(string path, string variant, Color tint, float size, float radius, bool area)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var oldVisual = root.GetComponent<ElementalVisual>();
            if (oldVisual != null) UnityEngine.Object.DestroyImmediate(oldVisual);
            var previous = root.GetComponent<SpellVfx>();
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var child = root.transform.Find("Imported spell VFX");
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            { particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var main = particles.main; main.playOnAwake = false; }
            AddVisual(root, variant, tint, size, radius, area);
            PrefabUtility.SaveAsPrefabAsset(root, path); changed.Add(path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static SpellVfx AddVisual(GameObject root, string variant, Color tint, float size, float radius, bool area)
    {
        string source = area ? Area : "Assets/VFX_FireballPack/Prefabs/Fireball_Static/S_Fireball_" + variant + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source);
        if (prefab == null) throw new Exception("Missing source effect: " + source);
        var child = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        child.name = "Imported spell VFX";
        child.SetActive(true);
        child.transform.localPosition = area ? new Vector3(0, -.2f, 0) : Vector3.zero;
        child.transform.localRotation = area ? Quaternion.identity : Quaternion.Euler(0, -90, 0);
        Vector3 scale = root.transform.lossyScale;
        child.transform.localScale = new Vector3(1 / Mathf.Max(.001f, Mathf.Abs(scale.x)), 1 / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1 / Mathf.Max(.001f, Mathf.Abs(scale.z)));
        var visual = root.AddComponent<SpellVfx>();
        visual.effect = child.GetComponent<VisualEffect>(); visual.tint = tint;
        visual.size = size; visual.radius = radius; visual.area = area;
        visual.Configure();
        PrefabUtility.RecordPrefabInstancePropertyModifications(visual.effect);
        PrefabUtility.RecordPrefabInstancePropertyModifications(child.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(child);
        return visual;
    }
}
