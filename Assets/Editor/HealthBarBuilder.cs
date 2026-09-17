using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class HealthBarBuilder
{
    public static void Apply()
    {
        const string path="Assets/Prefabs/UI/HealthBar.prefab";
        var root=new GameObject("HealthBar",typeof(RectTransform),typeof(Image));
        var rect=root.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(24,24);rect.sizeDelta=new Vector2(280,60);
        root.GetComponent<Image>().color=new Color(.035f,.05f,.08f,.9f);root.GetComponent<Image>().raycastTarget=false;
        var fill=new GameObject("Fill",typeof(RectTransform),typeof(Image));fill.transform.SetParent(root.transform,false);
        var fr=fill.GetComponent<RectTransform>();fr.anchorMin=Vector2.zero;fr.anchorMax=Vector2.one;fr.offsetMin=new Vector2(8,8);fr.offsetMax=new Vector2(-8,-32);
        var image=fill.GetComponent<Image>();image.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Horizontal;image.fillOrigin=0;image.color=new Color(.2f,.8f,.45f);image.raycastTarget=false;
        var label=new GameObject("Value",typeof(RectTransform),typeof(Text));label.transform.SetParent(root.transform,false);
        var lr=label.GetComponent<RectTransform>();lr.anchorMin=new Vector2(0,1);lr.anchorMax=Vector2.one;lr.pivot=new Vector2(.5f,1);lr.anchoredPosition=new Vector2(0,-3);lr.sizeDelta=new Vector2(-16,28);
        var text=label.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=18;text.color=Color.white;text.alignment=TextAnchor.MiddleLeft;text.text="Здоровье  100 / 100";text.raycastTarget=false;
        PrefabUtility.SaveAsPrefabAsset(root,path);UnityEngine.Object.DestroyImmediate(root);
        const string hudPath="Assets/Prefabs/UI/MatchUI.prefab";
        var hud=PrefabUtility.LoadPrefabContents(hudPath);
        try
        {
            var ui=hud.GetComponentInChildren<PlayerGameUI>(true);var data=new SerializedObject(ui);
            var old=data.FindProperty("status").objectReferenceValue as Text;
            var canvas=(Canvas)data.FindProperty("canvas").objectReferenceValue;
            var existing=canvas.transform.Find("HealthBar");if(existing!=null)UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var bar=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),canvas.transform);
            bar.transform.SetSiblingIndex(0);
            if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            data.Update();data.FindProperty("status").objectReferenceValue=bar.GetComponentInChildren<Text>();data.FindProperty("healthFill").objectReferenceValue=bar.transform.Find("Fill").GetComponent<Image>();data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(hud,hudPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(hud);}
        AssetDatabase.SaveAssets();Debug.Log("HEALTH_BAR_BUILT");
    }
    public static void InstallAll(){TacticalSpellBuilder.Apply();Apply();}
}
