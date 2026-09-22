using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// отдельная временная сцена позволяет проверить частицы и материалы, не трогая игровую арену.
public static class AdvancedSpellPreview
{
    [MenuItem("Tools/Wizard War/Preview advanced spells")]
    public static void Render()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play Mode.");
        Scene original=SceneManager.GetActiveScene();
        Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            string[] ids={"ShardVortex","ScaldingMist","Meteor","ThermalSpring","BoilingIce","IceBridge","CrystalCrash","SteamLens"};
            for(int i=0;i<ids.Length;i++)
            {
                var spell=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/"+ids[i]+".asset");
                var root=Object.Instantiate(spell.effectPrefab);
                root.transform.position=new Vector3(15000+(i%4)*9,15000,(i/4)*11+15000);
                var visual=root.GetComponent<AdvancedSpellVisual>();
                visual.area.SetActive(true);
                if(visual.warning!=null)visual.warning.SetActive(true);
                if(visual.fallingBody!=null){visual.fallingBody.SetActive(true);visual.fallingBody.transform.localPosition=Vector3.up*3;}
                foreach(var ps in root.GetComponentsInChildren<ParticleSystem>())ps.Simulate(1.2f,false,true);
                if(visual.impactPrefab!=null && spell.kind!=AdvancedSpellKind.SteamLens)
                {
                    var impact=Object.Instantiate(visual.impactPrefab,root.transform.position,Quaternion.identity);
                    foreach(var ps in impact.GetComponentsInChildren<ParticleSystem>())ps.Simulate(.3f,false,true);
                }
                if(visual.healthMarks!=null)for(int j=0;j<visual.healthMarks.Length;j++)visual.healthMarks[j].localPosition=new Vector3((j%3-1)*.65f,1+(j/3)*.6f,0);
            }
            var cameraObject=new GameObject("Preview camera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();
            Vector3 center=new Vector3(15013.5f,15000,15005.5f);camera.transform.position=center+new Vector3(0,26,-28);camera.transform.LookAt(center);
            camera.orthographic=true;camera.orthographicSize=13;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.06f);camera.cullingMask=1<<31;
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var target=new RenderTexture(1600,900,24);var previous=RenderTexture.active;var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes("Logs/advanced-spells-preview.png",image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(original.IsValid())SceneManager.SetActiveScene(original);}
    }
}
