using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// обновляет только оформление перезарядки и показывает разные стадии заполнения на копии интерфейса.
public static class CooldownStyleEditor
{
    private const string PrefabPath = "Assets/Prefabs/UI/MatchUI.prefab";

    // сохраняем изменения в исходном префабе, чтобы экземпляры сцены получили тот же внешний вид.
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("нужно остановить матч");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.GetComponentInChildren<PlayerGameUI>(true).EditorStyleCooldowns();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        RenderPreview();
        File.WriteAllText("Logs/cooldown-style-done.txt", DateTime.Now.ToString("O"));
    }

    // временная сцена позволяет проверить настоящие карточки без запуска матча и сохранения открытой сцены.
    private static void RenderPreview()
    {
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var data = new SerializedObject(root.GetComponentInChildren<PlayerGameUI>(true));
            var canvas = (Canvas)data.FindProperty("canvas").objectReferenceValue;
            canvas.gameObject.SetActive(true);
            canvas.enabled = true;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled = false;
            var rect = canvas.GetComponent<RectTransform>();
            rect.position = Vector3.zero;
            rect.rotation = Quaternion.identity;
            rect.localScale = Vector3.one * .01f;
            rect.sizeDelta = new Vector2(1280, 720);
            ((GameObject)data.FindProperty("pause").objectReferenceValue).SetActive(false);
            ((GameObject)data.FindProperty("scoreboard").objectReferenceValue).SetActive(false);
            var cards = data.FindProperty("cards");
            var sorted = Enumerable.Range(0, cards.arraySize).Select(i => cards.GetArrayElementAtIndex(i).Copy()).ToList();
            sorted.Sort((a, b) => Spell.CompareSimplicity(
                (Spell)a.FindPropertyRelative("spell").objectReferenceValue,
                (Spell)b.FindPropertyRelative("spell").objectReferenceValue));
            int index = 0;
            foreach (var card in sorted)
            {
                var spell = (Spell)card.FindPropertyRelative("spell").objectReferenceValue;
                var cardRoot = (GameObject)card.FindPropertyRelative("root").objectReferenceValue;
                cardRoot.transform.SetAsLastSibling();
                cardRoot.SetActive(spell.IsAvailable(ElementLoadout.Default));
                if (!cardRoot.activeSelf) continue;
                float remaining = 1 - (index % 5) * .25f;
                ((UnityEngine.UI.Image)card.FindPropertyRelative("cover").objectReferenceValue).fillAmount = remaining;
                ((UnityEngine.UI.Text)card.FindPropertyRelative("seconds").objectReferenceValue).text = remaining > 0 ? Mathf.CeilToInt(12 * remaining).ToString() : "";
                index++;
            }
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Canvas.ForceUpdateCanvases();
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            var cameraObject = new GameObject("камера карточек", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, -3.1f, -10);
            camera.orthographic = true;
            camera.orthographicSize = .65f;
            camera.aspect = 5;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.075f, .065f, .06f);
            var target = new RenderTexture(1600, 320, 24);
            var image = new Texture2D(1600, 320, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 320), 0, 0);
                image.Apply();
                File.WriteAllBytes("Logs/cooldown-style-preview.png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
        }
    }
}
