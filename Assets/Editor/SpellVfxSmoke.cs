using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SpellVfxSmoke
{
    const string Flag = "SpellVfxSmoke.Active";
    static double started, next;
    static readonly List<SpellVfx> visuals = new List<SpellVfx>();
    static int stage;
    static Camera camera;
    static SpellVfxSmoke() { EditorApplication.update += Tick; }
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (started == 0) { started = now; next = now + 1; }
            if (now - started > 75) throw new Exception("VFX smoke timeout");
            if (stage == 1 && camera != null)
            {
                foreach (var visual in visuals) visual.effect.Simulate(1f / 30f, 1);
                camera.Render();
            }
            if (now < next) return;
            if (stage == 0)
            {
                camera = new GameObject("VFX test camera").AddComponent<Camera>();
                camera.targetTexture = new RenderTexture(640, 480, 24);
                camera.transform.position = new Vector3(0, 15, -24); camera.transform.LookAt(new Vector3(0, 0, 10));
                camera.farClipPlane = 100; camera.fieldOfView = 75;
                int index = 0;
                foreach (string path in File.ReadAllLines("Logs/spell-vfx-files.txt"))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var go = UnityEngine.Object.Instantiate(prefab, new Vector3((index % 4) * 6 - 9, 1, (index / 4) * 6), Quaternion.identity);
                    if (go.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = true;
                    visuals.Add(go.GetComponent<SpellVfx>()); index++;
                }
                foreach (var visual in visuals) { visual.effect.Reinit(); visual.effect.Play(); }
                stage = 1; next = EditorApplication.timeSinceStartup + 4;
            }
            else
            {
                foreach (var visual in visuals)
                {
                    var systems = new List<string>(); visual.effect.GetParticleSystemNames(systems);
                    Debug.Log("VFX_DIAGNOSTIC " + visual.name + ": particles=" + visual.effect.aliveParticleCount + ", systems=" + systems.Count + ", active=" + visual.effect.isActiveAndEnabled + ", paused=" + visual.effect.pause);
                }
                foreach (var visual in visuals)
                {
                    if (!visual.effect.isActiveAndEnabled || visual.effect.aliveParticleCount <= 0)
                        throw new Exception("VFX has no live particles: " + visual.name + "; culled=" + visual.effect.culled);
                }
                Debug.Log("SPELL_VFX_SMOKE_PASSED: " + visuals.Count + " effects emit particles in Play Mode");
                SessionState.SetBool(Flag, false); EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Flag, false); EditorApplication.Exit(1); }
    }
}
