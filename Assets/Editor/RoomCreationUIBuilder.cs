using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class RoomCreationUIBuilder
{
    public static void Apply()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
        var menu=UnityEngine.Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        if(menu==null)throw new Exception("MainMenuUI missing");
        var existing=scene.GetRootGameObjects();
        foreach(var go in existing)if(go.name=="Create Room UI")throw new Exception("Dialog already exists; preserve manual edits");
        var root=new GameObject("Create Room UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=2000;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var shade=Box(root.transform,"Backdrop",Vector2.zero,new Vector2(1920,1080),new Color(0,0,0,.65f));
        shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.sizeDelta=Vector2.zero;
        var card=Box(root.transform,"Create room dialog",Vector2.zero,new Vector2(600,320),new Color(.07f,.09f,.15f,1));
        Label(card,"Title","Создать комнату",new Vector2(0,110),new Vector2(540,45),32);
        Label(card,"Room name label","Название комнаты",new Vector2(0,52),new Vector2(520,30),22);
        var field=Box(card,"Room name",new Vector2(0,0),new Vector2(520,55),new Color(.15f,.19f,.28f,1));
        var viewport=new GameObject("Text Area",typeof(RectTransform),typeof(RectMask2D)).GetComponent<RectTransform>();viewport.SetParent(field,false);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=new Vector2(14,5);viewport.offsetMax=new Vector2(-14,-5);
        var text=Label(viewport,"Text","",Vector2.zero,new Vector2(490,45),24);text.alignment=TextAlignmentOptions.MidlineLeft;text.richText=false;
        var placeholder=Label(viewport,"Placeholder","Например: Дуэль магов",Vector2.zero,new Vector2(490,45),24);placeholder.alignment=TextAlignmentOptions.MidlineLeft;placeholder.color=new Color(.65f,.7f,.8f,1);
        var input=field.gameObject.AddComponent<TMP_InputField>();input.textViewport=viewport;input.textComponent=text;input.placeholder=placeholder;input.targetGraphic=field.GetComponent<Image>();input.characterLimit=32;input.lineType=TMP_InputField.LineType.SingleLine;
        var cancel=Button(card,"Cancel","Отмена",new Vector2(-140,-105));var confirm=Button(card,"Create","Создать",new Vector2(140,-105));
        UnityEventTools.AddPersistentListener(cancel.onClick,menu.CancelCreateRoom);UnityEventTools.AddPersistentListener(confirm.onClick,menu.ConfirmCreateRoom);
        var so=new SerializedObject(menu);so.FindProperty("createRoomPanel").objectReferenceValue=root;so.FindProperty("roomNameInput").objectReferenceValue=input;so.FindProperty("confirmRoomButton").objectReferenceValue=confirm;so.ApplyModifiedPropertiesWithoutUndo();
        foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;
        root.SetActive(false);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Debug.Log("ROOM_CREATION_UI_SAVED");
    }
    static RectTransform Box(Transform parent,string name,Vector2 pos,Vector2 size,Color color)
    {var go=new GameObject(name,typeof(RectTransform),typeof(Image));var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=pos;go.GetComponent<Image>().color=color;return rect;}
    static TextMeshProUGUI Label(Transform parent,string name,string value,Vector2 pos,Vector2 size,float fontSize)
    {var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));var text=go.GetComponent<TextMeshProUGUI>();text.rectTransform.SetParent(parent,false);text.rectTransform.sizeDelta=size;text.rectTransform.anchoredPosition=pos;text.text=value;text.font=TMP_Settings.defaultFontAsset;text.fontSize=fontSize;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;return text;}
    static Button Button(Transform parent,string name,string caption,Vector2 pos)
    {var rect=Box(parent,name,pos,new Vector2(240,55),new Color(.2f,.3f,.5f,1));var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();Label(rect,"Label",caption,Vector2.zero,new Vector2(225,48),24);return button;}
}
