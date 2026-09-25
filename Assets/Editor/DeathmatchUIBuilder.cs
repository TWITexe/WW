using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Extends saved UI objects without rebuilding the existing menu or HUD.
public static class DeathmatchUIBuilder
{
    static readonly Color PanelColor = new Color(.055f,.055f,.055f,.86f);
    static readonly Color FieldColor = new Color(.15f,.085f,.035f,.9f);
    static readonly Color BrownPanel = new Color(.12f,.065f,.02f,.88f);
    [MenuItem("Tools/Wizard War/Install room and deathmatch options")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new Exception("Stop Play Mode before installing UI");
        Menu();
            using (var scope = new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/UI/MatchUI.prefab"))
                Hud(scope.prefabContentsRoot.GetComponent<PlayerGameUI>());
            AssetDatabase.SaveAssets();
        Debug.Log("DEATHMATCH_UI_INSTALLED");
    }
    static void Menu()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Menu.unity");
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Additive);
        if (scene.isDirty)
        {
            System.IO.Directory.CreateDirectory("Logs/DeathmatchBefore");
            EditorSceneManager.SaveScene(scene, "Logs/DeathmatchBefore/Menu.unsaved.unity", true);
        }
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        var so = new SerializedObject(menu);
        var root = (GameObject)so.FindProperty("createRoomPanel").objectReferenceValue;
        var card = root.transform.Find("Create room dialog").GetComponent<RectTransform>();
        card.sizeDelta = new Vector2(640, 800);
        card.GetComponent<UnityEngine.UI.Image>().color = BrownPanel;
        card.Find("Room name").GetComponent<UnityEngine.UI.Image>().color = FieldColor;
        foreach (string buttonName in new[] { "Cancel", "Create" })
        {
            var button = card.Find(buttonName).GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic.color = FieldColor;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1,.85f,.6f); button.colors = colors;
        }
        Move(card.Find("Title"), 0, 340);
        Move(card.Find("Room name label"), 0, 280);
        Move(card.Find("Room name"), 0, 230);
        Move(card.Find("Cancel"), -140, -340);
        Move(card.Find("Create"), 140, -340);
        var privacy = Box(card, "Private room", -240, 162, 34, 34, FieldColor);
        privacy.GetComponent<UnityEngine.UI.Image>().color = new Color(.35f,.23f,.12f,.95f);
        var toggle = privacy.GetComponent<UnityEngine.UI.Toggle>() ?? privacy.gameObject.AddComponent<UnityEngine.UI.Toggle>();
        var mark = Box(privacy, "Check", 0, 0, 22, 22, new Color(.85f,.61f,.3f));
        toggle.targetGraphic = privacy.GetComponent<UnityEngine.UI.Image>(); toggle.graphic = mark.GetComponent<UnityEngine.UI.Image>();
        Label(card,"Privacy label","Закрытая комната", 40,162,440,40,24);
        var password = Input(card,"Room password","Пароль комнаты",0,104,520,50,true);
        Label(card,"Mode label","Режим игры",0,43,520,30,22);
        var existing = card.Find("Game mode");
        var modeRoot = existing != null ? existing.gameObject : TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        modeRoot.name = "Game mode"; modeRoot.transform.SetParent(card,false);
        var modeRect = (RectTransform)modeRoot.transform; modeRect.anchorMin = modeRect.anchorMax = new Vector2(.5f,.5f);
        modeRect.anchoredPosition = new Vector2(0,-3); modeRect.sizeDelta = new Vector2(520,50);
        var mode = modeRoot.GetComponent<TMP_Dropdown>();
        mode.ClearOptions(); mode.AddOptions(new System.Collections.Generic.List<string>{"Схватка — каждый сам за себя"});
        foreach(var text in modeRoot.GetComponentsInChildren<TMP_Text>(true)) { text.font = TMP_Settings.defaultFontAsset; text.fontSize = 23; text.color = Color.white; }
        modeRoot.GetComponent<UnityEngine.UI.Image>().color = FieldColor;
        var arrow = modeRoot.transform.Find("Arrow");
        if (arrow != null)
        {
            arrow.GetComponent<UnityEngine.UI.Image>().enabled = false;
            Label(arrow,"Arrow label","v",0,0,24,28,20);
        }
        foreach (var background in modeRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            if (background.transform.name == "Template" || background.transform.name == "Item Background") background.color = FieldColor;
        Label(card,"Minutes label","Время игры · 1–30 минут",0,-71,520,32,22);
        var minutes = Input(card,"Match minutes","10",224,-114,72,48,false);
        minutes.contentType = TMP_InputField.ContentType.IntegerNumber; minutes.characterLimit = 2; minutes.text = "10";
        minutes.readOnly = true; minutes.textComponent.alignment = TextAlignmentOptions.Center;
        var minutesSlider = Slider(card,"Minutes slider",-48,-114,398,1,30,10);
        Label(card,"Kills label","Цель · 1–33 убийства",0,-178,520,32,22);
        var kills = Input(card,"Kill goal","15",224,-221,72,48,false);
        kills.contentType = TMP_InputField.ContentType.IntegerNumber; kills.characterLimit = 2; kills.text = "15";
        kills.readOnly = true; kills.textComponent.alignment = TextAlignmentOptions.Center;
        var killsSlider = Slider(card,"Kills slider",-48,-221,398,1,33,15);
        Label(card,"Finish hint","Матч завершится по времени или цели убийств",0,-279,580,34,19);
        Set(so,"privateRoomToggle",toggle); Set(so,"roomPasswordInput",password); Set(so,"matchMinutesInput",minutes);
        Set(so,"killGoalInput",kills); Set(so,"gameModeDropdown",mode);
        Set(so,"matchMinutesSlider",minutesSlider); Set(so,"killGoalSlider",killsSlider);
        var joinRoot = scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Join Room UI");
        if (joinRoot == null)
        {
            joinRoot = new GameObject("Join Room UI",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            joinRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; joinRoot.GetComponent<Canvas>().sortingOrder = 2100;
            var scale = joinRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scale.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920,1080); scale.matchWidthOrHeight = .5f;
        }
        var shade = Box(joinRoot.transform,"Backdrop",0,0,0,0,new Color(0,0,0,.7f)); Stretch(shade);
        var join = Box(joinRoot.transform,"Join dialog",0,0,640,380,BrownPanel);
        var title = Label(join,"Title","Вход в комнату",0,132,560,46,30);
        var joinPassword = Input(join,"Password","Пароль",0,61,520,54,true);
        var status = Label(join,"Status","Введите пароль комнаты",0,-20,560,85,23);
        var joinButton = Button(join,"Join","Войти",140,-125,240,55);
        var cancel = Button(join,"Cancel","В меню",-140,-125,240,55);
        Wire(joinButton,menu.ConfirmJoinRoom); Wire(cancel,menu.CancelJoinRoom);
        Set(so,"joinRoomPanel",joinRoot); Set(so,"joinPasswordInput",joinPassword); Set(so,"joinTitle",title);
        Set(so,"joinStatus",status); Set(so,"joinConfirmButton",joinButton);
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(menu);
        root.SetActive(false); joinRoot.SetActive(false);
        Layers(root); Layers(joinRoot);
        EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }
    static void Hud(PlayerGameUI ui)
    {
        var so = new SerializedObject(ui);
        var canvas = (Canvas)so.FindProperty("canvas").objectReferenceValue;
        var timer = Label(canvas.transform,"Match timer","10:00",0,-40,260,55,38);
        Top(timer.rectTransform,0,-40); timer.color = Color.white;
        var caption = Label(canvas.transform,"Match rules","Схватка · до 15 убийств",0,-79,440,30,18);
        Top(caption.rectTransform,0,-79);
        var results = Box(canvas.transform,"Match results",0,0,0,0,new Color(0,0,0,.48f)); Stretch(results);
        var card = Box(results,"Results card",0,0,1040,620,PanelColor);
        Label(card,"Title","ИТОГ СХВАТКИ",0,260,940,46,30);
        var reason = Label(card,"Reason","",0,215,940,35,22);
        Label(card,"Name header","Игрок",-210,161,520,35,22).alignment = TextAlignmentOptions.MidlineLeft;
        Label(card,"Kill header","Убийства",180,161,160,35,22);
        Label(card,"Death header","Смерти",375,161,150,35,22);
        var scrollRoot = Box(card,"Results scroll",0,-13,940,300,Color.clear);
        var viewport = Box(scrollRoot,"Viewport",0,0,0,0,Color.clear); Stretch(viewport);
        if (viewport.GetComponent<UnityEngine.UI.RectMask2D>() == null) viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var content = Box(viewport,"Content",0,0,0,300,Color.clear);
        content.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0,1); content.anchoredPosition = Vector2.zero;
        var scroll = scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>() ?? scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 35;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        var names = Column(content,"Names",0,530); var kills = Column(content,"Kills",570,160); var deaths = Column(content,"Deaths",770,150);
        kills.alignment = deaths.alignment = TextAlignmentOptions.Top;
        var status = Label(card,"Rematch status","",0,-205,940,70,22);
        var menu = Button(card,"Menu","В меню",-245,-270,410,50);
        var rematch = Button(card,"Rematch","Реванш",245,-270,410,50);
        menu.targetGraphic.color = rematch.targetGraphic.color = new Color(.15f,.15f,.15f,.82f);
        var cards = so.FindProperty("cards");
        for (int i=0;i<cards.arraySize;i++)
        {
            var root=(GameObject)cards.GetArrayElementAtIndex(i).FindPropertyRelative("root").objectReferenceValue;
            if (root != null && root.TryGetComponent<UnityEngine.UI.Image>(out var background)) background.color=new Color(.055f,.055f,.055f,.78f);
        }
        Set(so,"matchTimer",timer); Set(so,"matchCaption",caption); Set(so,"resultsPanel",results.gameObject);
        Set(so,"resultReason",reason); Set(so,"resultNames",names); Set(so,"resultKills",kills); Set(so,"resultDeaths",deaths);
        Set(so,"resultsContent",content); Set(so,"rematchStatus",status); Set(so,"rematchButton",rematch); Set(so,"resultsMenuButton",menu);
        so.ApplyModifiedPropertiesWithoutUndo(); results.gameObject.SetActive(false); Layers(results.gameObject);
    }
    static TMP_Text Column(Transform parent,string name,float x,float width)
    {
        var text=Label(parent,name,"",0,0,width,300,24); var rect=text.rectTransform;
        rect.anchorMin=rect.anchorMax=new Vector2(0,1); rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(x,0);
        text.alignment=TextAlignmentOptions.TopLeft; text.textWrappingMode=TextWrappingModes.NoWrap; text.lineSpacing=8;
        text.overflowMode=TextOverflowModes.Ellipsis; return text;
    }
    static void Top(RectTransform rect,float x,float y) { rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x,y); }
    static void Set(SerializedObject so,string name,UnityEngine.Object value) => so.FindProperty(name).objectReferenceValue=value;
    static void Move(Transform target,float x,float y) => ((RectTransform)target).anchoredPosition=new Vector2(x,y);
    static void Stretch(RectTransform rect) { rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero; }
    static void Layers(GameObject root) { foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=5; }
    static RectTransform Box(Transform parent,string name,float x,float y,float width,float height,Color color)
    {
        var child=parent.Find(name);
        var go=child!=null?child.gameObject:new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image));
        var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
        rect.sizeDelta=new Vector2(width,height);rect.anchoredPosition=new Vector2(x,y);go.GetComponent<UnityEngine.UI.Image>().color=color;
        return rect;
    }
    static TMP_Text Label(Transform parent,string name,string value,float x,float y,float width,float height,float size)
    {
        var child=parent.Find(name);
        var go=child!=null?child.gameObject:new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));
        var text=go.GetComponent<TMP_Text>();text.rectTransform.SetParent(parent,false);text.rectTransform.anchorMin=text.rectTransform.anchorMax=new Vector2(.5f,.5f);
        text.rectTransform.sizeDelta=new Vector2(width,height);text.rectTransform.anchoredPosition=new Vector2(x,y);
        text.font=TMP_Settings.defaultFontAsset;text.fontSize=size;text.text=value;text.richText=false;
        text.alignment=TextAlignmentOptions.Center;text.color=Color.white;text.raycastTarget=false;return text;
    }
    static TMP_InputField Input(Transform parent,string name,string placeholder,float x,float y,float width,float height,bool password)
    {
        var rect=Box(parent,name,x,y,width,height,FieldColor);
        var area=Box(rect,"Text area",0,0,0,0,Color.clear);Stretch(area);area.offsetMin=new Vector2(14,4);area.offsetMax=new Vector2(-14,-4);
        area.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        if(area.GetComponent<UnityEngine.UI.RectMask2D>()==null)area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var text=Label(area,"Text","",0,0,width-28,height-8,24);text.alignment=TextAlignmentOptions.MidlineLeft;
        var hint=Label(area,"Placeholder",placeholder,0,0,width-28,height-8,22);hint.alignment=TextAlignmentOptions.MidlineLeft;hint.color=new Color(.65f,.7f,.8f);
        var input=rect.GetComponent<TMP_InputField>()??rect.gameObject.AddComponent<TMP_InputField>();
        input.textViewport=area;input.textComponent=text;input.placeholder=hint;input.targetGraphic=rect.GetComponent<UnityEngine.UI.Image>();
        input.contentType=password?TMP_InputField.ContentType.Password:TMP_InputField.ContentType.Standard;input.characterLimit=64;
        return input;
    }
    static UnityEngine.UI.Button Button(Transform parent,string name,string caption,float x,float y,float width,float height)
    {
        var rect=Box(parent,name,x,y,width,height,FieldColor);
        var button=rect.GetComponent<UnityEngine.UI.Button>()??rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic=rect.GetComponent<UnityEngine.UI.Image>();Label(rect,"Label",caption,0,0,width-16,height-4,24);return button;
    }
    static void Wire(UnityEngine.UI.Button button,UnityEngine.Events.UnityAction action)
    {
        for(int i=button.onClick.GetPersistentEventCount()-1;i>=0;i--)UnityEventTools.RemovePersistentListener(button.onClick,i);
        UnityEventTools.AddPersistentListener(button.onClick,action);
    }
    static UnityEngine.UI.Slider Slider(Transform parent,string name,float x,float y,float width,int min,int max,int value)
    {
        var rect=Box(parent,name,x,y,width,40,Color.clear);
        var track=Box(rect,"Track",0,0,width,8,new Color(.04f,.02f,.01f,.95f));
        var fillArea=Box(rect,"Fill area",0,0,width-20,8,Color.clear);
        var fill=Box(fillArea,"Fill",0,0,0,0,new Color(.68f,.43f,.18f));Stretch(fill);
        var handleArea=Box(rect,"Handle area",0,0,width-20,40,Color.clear);
        var handle=Box(handleArea,"Handle",0,0,20,30,new Color(.94f,.72f,.4f));
        foreach(var graphic in new[]{track,fillArea,fill,handleArea})graphic.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        var slider=rect.GetComponent<UnityEngine.UI.Slider>()??rect.gameObject.AddComponent<UnityEngine.UI.Slider>();
        slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<UnityEngine.UI.Image>();
        slider.minValue=min;slider.maxValue=max;slider.wholeNumbers=true;slider.value=value;handle.sizeDelta=new Vector2(20,-10);return slider;
    }
}

