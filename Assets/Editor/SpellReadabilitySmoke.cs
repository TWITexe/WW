using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SpellReadabilitySmoke
{
    const string Flag="SpellReadabilitySmoke";
    static double next;
    static int stage, checks, frames;
    static Camera camera;
    static readonly List<GameObject> objects=new List<GameObject>();
    static SpellReadabilitySmoke(){EditorApplication.update+=Tick;}
    public static void Run(){SpellReadabilityTuning.Apply();SessionState.SetBool(Flag,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            if(stage==1)
            {
                foreach(var root in objects)
                    foreach(var particles in root.GetComponentsInChildren<ParticleSystem>()) particles.Simulate(1f/30f, false, false);
                camera.Render();
                if (++frames < 30) return;
            }
            if(EditorApplication.timeSinceStartup<next)return;
            if(stage==0)
            {
                camera=new GameObject("Readability camera").AddComponent<Camera>();
                camera.transform.position=new Vector3(0,15,-24);camera.transform.LookAt(new Vector3(0,1,10));
                camera.targetTexture=new RenderTexture(1000,700,24);camera.backgroundColor=new Color(.08f,.1f,.15f);camera.clearFlags=CameraClearFlags.SolidColor;
                var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(50,-30,0);
                int i=0;
                foreach(string path in File.ReadAllLines("Logs/spell-readability-files.txt").Where(p=>p.EndsWith(".prefab")))
                {
                    var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),new Vector3(i%4*6-9,1,i/4*6),Quaternion.identity);
                    root.GetComponent<Rigidbody>().isKinematic=true;objects.Add(root);i++;
                }
                SpellVfx.Burst(new Vector3(-9,1,18),Color.cyan,3,3);
                next=EditorApplication.timeSinceStartup+2;stage=1;
            }
            else
            {
                foreach(var root in objects)
                {
                    var visual=root.GetComponent<ElementalVisual>();
                    Check(visual!=null,"Missing visual "+root.name);
                    Check(root.GetComponentsInChildren<ParticleSystem>().Any(p=>p.particleCount>0),"No ordinary particles "+root.name);
                    if(visual.projectile)Check(root.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled),"No visible projectile core "+root.name);
                    else if(root.GetComponent<ElementalEffect>().definition.mode==ElementalCastMode.Tornado)
                        Check(root.GetComponentsInChildren<LineRenderer>().Any(r=>r.name=="Fire spiral" && r.enabled && r.positionCount==90 && Mathf.Abs(r.GetPosition(89).y-7)<.01f),"Tornado height");
                    else Check(root.GetComponentsInChildren<LineRenderer>().Any(r=>r.loop&&r.positionCount==96),"Area boundary missing");
                }
                Check(UnityEngine.Object.FindObjectsByType<SpellVfx>(FindObjectsSortMode.None).Any(v=>v.impact),"Frost visual disappeared before 2s");
                RenderTexture.active=camera.targetTexture;var image=new Texture2D(1000,700,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,700),0,0);image.Apply();File.WriteAllBytes("Logs/spell-readability.png",image.EncodeToPNG());
                Debug.Log("SPELL_READABILITY_SMOKE_PASSED: "+checks+" checks");SessionState.SetBool(Flag,false);EditorApplication.Exit(0);
            }
        }
        catch(Exception e){Debug.LogException(e);SessionState.SetBool(Flag,false);EditorApplication.Exit(1);}
    }
}
