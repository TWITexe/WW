using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class CameraHudTuning
{
    public static void Apply()
    {
        GameplayPolishBuilder.Apply();
        const string path="Assets/Prefabs/UI/MatchUI.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var ui=root.GetComponent<PlayerGameUI>();var so=new SerializedObject(ui);
            var cards=so.FindProperty("cards");
            for(int i=0;i<cards.arraySize;i++)
            {
                var card=cards.GetArrayElementAtIndex(i);
                var panel=((GameObject)card.FindPropertyRelative("root").objectReferenceValue).GetComponent<RectTransform>();
                var icon=(SpellIconGraphic)card.FindPropertyRelative("icon").objectReferenceValue;
                var cover=(Image)card.FindPropertyRelative("cover").objectReferenceValue;
                var seconds=(Text)card.FindPropertyRelative("seconds").objectReferenceValue;
                var keys=(Text)card.FindPropertyRelative("keys").objectReferenceValue;
                panel.sizeDelta=new Vector2(48,64);
                icon.rectTransform.sizeDelta=Vector2.one*(64f/3);icon.rectTransform.anchoredPosition=new Vector2(-32f/3,26);
                cover.rectTransform.sizeDelta=Vector2.one*(64f/3);
                seconds.rectTransform.sizeDelta=Vector2.one*(64f/3);seconds.fontSize=14;
                keys.rectTransform.sizeDelta=new Vector2(44,20);keys.rectTransform.anchoredPosition=new Vector2(2,2);keys.fontSize=14;
                foreach(var text in panel.GetComponentsInChildren<Text>(true))if(text!=keys&&text!=seconds)text.gameObject.SetActive(false);
            }
            var row=(RectTransform)root.transform.Find("Player HUD/Cooldown bar");row.sizeDelta=new Vector2(row.sizeDelta.x,64);
            var combo=(Text)so.FindProperty("combo").objectReferenceValue;combo.fontSize=18;
            combo.rectTransform.anchoredPosition=new Vector2(combo.rectTransform.anchoredPosition.x,90);combo.rectTransform.sizeDelta=new Vector2(combo.rectTransform.sizeDelta.x,48);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();Debug.Log("CAMERA_HUD_TUNED");
    }
}
