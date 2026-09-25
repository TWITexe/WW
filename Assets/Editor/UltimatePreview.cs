using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class UltimatePreview
{
    [MenuItem("Tools/Wizard War/Preview ultimate visuals")]
    public static void Render()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode for the preview.");
        Scene original = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            Vector3 start = new Vector3(15000,15000,15000);
            var catalog = AssetDatabase.LoadAssetAtPath<UltimateCatalog>("Assets/Spells/Ultimates/UltimateCatalog.asset");
            var cameraObject = new GameObject("Ultimate preview camera",typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            Vector3 center = start + new Vector3(15,0,7.5f);
            camera.transform.position = center + new Vector3(0,30,-32); camera.transform.LookAt(center);
            camera.orthographic = true; camera.orthographicSize = 15.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.045f,.065f); camera.cullingMask = 1 << 31;
            var light = new GameObject("Preview key",typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 2; light.transform.rotation = Quaternion.Euler(45,-30,0);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            string[] titles = { "Рождение Феникса", "Дух 3 стихий", "Ледниковый таран", "Недра земли", "Зеркальный лабиринт", "Переворот притяжения" };
            for (int i = 0; i < 6; i++)
            {
                Vector3 position = start + new Vector3((i%3)*15,0,(i/3)*15);
                GameObject root;
                if (i < 2)
                {
                    var view = player.GetComponentInChildren<PlayerUltimate>(true).presentation;
                    root = Object.Instantiate(i == 0 ? view.meteor : view.spirit);
                    root.SetActive(true); root.transform.position = position; root.transform.localScale = Vector3.one * 1.7f;
                }
                else
                {
                    UltimateKind kind = i == 2 ? UltimateKind.GlacierRam : i == 3 ? UltimateKind.EarthDepths : i == 4 ? UltimateKind.MirrorLabyrinth : UltimateKind.GravityInversion;
                    root = Object.Instantiate(catalog.Get(kind).worldPrefab, position, Quaternion.identity);
                }
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(.5f,false,true);
                var label = new GameObject(titles[i],typeof(TextMeshPro)).GetComponent<TextMeshPro>();
                label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/SavedBuildNamesFont.asset");
                label.text = titles[i]; label.fontSize = 3.5f; label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta = new Vector2(13,2); label.transform.position = position + new Vector3(0,0,-6.7f);
                label.transform.rotation = camera.transform.rotation;
            }
            foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            var target = new RenderTexture(1920,1080,24); var image = new Texture2D(1920,1080,TextureFormat.RGB24,false); var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply();
                File.WriteAllBytes("Logs/ultimate-visuals-preview.png",image.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(target); Object.DestroyImmediate(image); }
        }
        finally { EditorSceneManager.CloseScene(scene,true); if(original.IsValid())SceneManager.SetActiveScene(original); }
    }
}
