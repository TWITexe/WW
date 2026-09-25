using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class UltimateRevisionPreview
{
    public static void Render()
    {
        var original=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            Vector3 origin=new Vector3(0,150,0);
            var camera=new GameObject("Model preview",typeof(Camera)).GetComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=2.45f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.065f,.085f);
            camera.cullingMask=1<<31;camera.farClipPlane=100;camera.nearClipPlane=.1f;
            var key=new GameObject("Key",typeof(Light)).GetComponent<Light>();key.type=LightType.Directional;key.intensity=2.2f;key.transform.rotation=Quaternion.Euler(30,-135,0);key.cullingMask=1<<31;
            var rim=new GameObject("Rim",typeof(Light)).GetComponent<Light>();rim.type=LightType.Directional;rim.color=new Color(.5f,.8f,1);rim.intensity=1;rim.transform.rotation=Quaternion.Euler(25,45,0);rim.cullingMask=1<<31;
            var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spells/Ultimates/ElementalSpiritModel.prefab"));model.transform.position=origin;
            foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            model.GetComponent<Animation>().clip.SampleAnimation(model,.7f);
            camera.transform.position=origin+new Vector3(4,2.8f,7);camera.transform.LookAt(origin+Vector3.up*.75f);
            Capture(camera,"Logs/ultimate-spirit-revised.png");Object.DestroyImmediate(model);
            model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spells/Ultimates/EarthDepths.prefab"));model.transform.position=origin;
            foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            camera.orthographicSize=4;camera.transform.position=origin+new Vector3(7,5,9);camera.transform.LookAt(origin+Vector3.down*.7f);
            Capture(camera,"Logs/ultimate-island-revised.png");
        }
        finally {EditorSceneManager.CloseScene(scene,true);if(original.IsValid())SceneManager.SetActiveScene(original);}
    }
    static void Capture(Camera camera,string path)
    {
        var rt=new RenderTexture(1200,1200,24);var image=new Texture2D(1200,1200,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1200,1200),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally {camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
}
