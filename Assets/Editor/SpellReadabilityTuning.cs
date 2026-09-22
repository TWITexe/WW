using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// сохраняет размеры и материалы более заметных снарядов и областей заклинаний.
public static class SpellReadabilityTuning
{
    static readonly List<string> changed = new List<string>();
    // обновляем материал частиц, длительность наземных зон и оформление префабов, затем сохраняем ассеты.
    [MenuItem("Wizard/VFX/Apply clearer spell visuals")]
    public static void Apply()
    {
        changed.Clear();
        const string particlePath = "Assets/Other Asstets/GeneratedWizard/ReadableSpellParticles.mat";
        var particles = AssetDatabase.LoadAssetAtPath<Material>(particlePath);
        if (particles == null) { particles = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Other Asstets/GeneratedWizard/WaterBoltParticles.mat")); AssetDatabase.CreateAsset(particles, particlePath); }
        particles.SetColor("_BaseColor", Color.white); particles.SetColor("_EmissionColor", Color.black);
        particles.DisableKeyword("_EMISSION"); EditorUtility.SetDirty(particles); changed.Add(particlePath);
        Tune("Assets/Prefabs/FireBall.prefab", null, particles, new Color(1, .3f, .04f));
        Tune("Assets/Prefabs/WindFlow.prefab", null, particles, new Color(.5f, 1, .85f));
        foreach (string guid in AssetDatabase.FindAssets("t:ElementalSpell", new[] {"Assets/Scripts/Spells/Elemental"}))
        {
            var spell = AssetDatabase.LoadAssetAtPath<ElementalSpell>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell.effectPrefab == null) continue;
            if (spell.mode == ElementalCastMode.GroundZone)
            {
                // задаём абсолютные значения, чтобы повторный запуск не накапливал изменения.
                spell.duration = spell.name == "Blizzard" ? 7.5f : spell.name == "Mud" ? 7.5f : spell.name == "Geyser" ? 3.5f : 6.5f;
                EditorUtility.SetDirty(spell); changed.Add(AssetDatabase.GetAssetPath(spell));
            }
            if (spell.mode != ElementalCastMode.SelfBurst)
                Tune(AssetDatabase.GetAssetPath(spell.effectPrefab), spell, particles, spell.tint);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllLines("Logs/spell-readability-files.txt", changed);
        Debug.Log("SPELL_READABILITY_APPLIED: " + changed.Count + " assets");
    }
    // настраиваем графику выбранного префаба без изменения его коллайдера и сохраняем результат.
    static void Tune(string path, ElementalSpell spell, Material particles, Color tint)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool bolt = spell == null || spell.mode == ElementalCastMode.Bolt;
            if (!bolt)
            {
                var imported = root.transform.Find("Imported spell VFX");
                if (imported != null) UnityEngine.Object.DestroyImmediate(imported.gameObject);
                var graph = root.GetComponent<SpellVfx>(); if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
            }
            else
                foreach (var mesh in root.GetComponentsInChildren<MeshRenderer>(true)) mesh.enabled = true;
            var visual = root.GetComponent<ElementalVisual>();
            if (visual == null) visual = root.AddComponent<ElementalVisual>();
            visual.particleMaterial = particles; visual.projectile = bolt; visual.fallbackTint = tint;
            visual.tornadoHeight = 7;
            visual.projectileVisualScale = bolt ? 2.5f : 1;
            var importedVfx = root.GetComponent<SpellVfx>();
            if (bolt && importedVfx != null)
            {
                importedVfx.size = (root.name == "FireBall" ? .65f : root.name == "WindFlow" ? .75f : root.name == "Boulder" ? .9f : .6f) * 2.5f;
                importedVfx.Configure();
            }
            string materialPath = "Assets/Other Asstets/GeneratedWizard/Readable_" + root.name + ".mat";
            var line = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (line == null) { line = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(line, materialPath); }
            line.SetColor("_BaseColor", tint * (bolt ? 1.5f : 1f)); EditorUtility.SetDirty(line); changed.Add(materialPath);
            visual.lineMaterial = line;
            if (bolt && root.GetComponentsInChildren<MeshRenderer>(true).Length == 0)
            {
                var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                core.name = "Visible projectile core";
                UnityEngine.Object.DestroyImmediate(core.GetComponent<Collider>());
                core.transform.SetParent(root.transform, false);
                Vector3 scale = root.transform.lossyScale;
                core.transform.localScale = new Vector3(.35f / Mathf.Abs(scale.x), .35f / Mathf.Abs(scale.y), .35f / Mathf.Abs(scale.z));
                core.GetComponent<MeshRenderer>().sharedMaterial = line;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path); changed.Add(path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}

