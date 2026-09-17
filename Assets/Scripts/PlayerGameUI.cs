using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public class PlayerGameUI : MonoBehaviour
{
    public static bool InputBlocked { get; private set; }
    private PlayerNetworkCaster caster;
    private SpellManager spells;
    private Health health;
    [SerializeField] private Canvas canvas;
    [SerializeField] private GameObject pause, scoreboard;
    [SerializeField] private Text status, combo, table;
    [SerializeField] private Image healthFill;
    [SerializeField] private Text killColumn, deathColumn, pingColumn;
    [SerializeField] private RectTransform scoreContent;
    [SerializeField] private Button resumeButton, menuButton, quitButton;
    private Sprite fillSprite;
    private Font font;
    private bool built;
    private float nextScore;
    [SerializeField] private List<Card> cards=new List<Card>();
    [System.Serializable]
    private class Card { public Spell spell; public GameObject root; public Text keys; public Image cover; public Text seconds; public SpellIconGraphic icon; }
    private void Start()
    {
        canvas.enabled=false;
        pause.SetActive(false);scoreboard.SetActive(false);
        resumeButton.onClick.AddListener(()=>SetPause(false));
        menuButton.onClick.AddListener(ReturnToMenu);
        quitButton.onClick.AddListener(QuitGame);
    }
    private void Update()
    {
        if(caster==null)
        {
            if(NetworkClient.localPlayer==null)return;
            caster=NetworkClient.localPlayer.GetComponentInChildren<PlayerNetworkCaster>();
            if(caster==null)return;
            spells=caster.GetComponent<SpellManager>();health=caster.GetComponent<Health>();
            canvas.enabled=true;built=false;SetPause(false);
        }
        if(Input.GetKeyDown(KeyCode.Escape))SetPause(!InputBlocked);
        scoreboard.SetActive(!InputBlocked&&Input.GetKey(KeyCode.Tab));
        if(caster.LoadoutReady&&!built)
        {
            foreach(var card in cards)
            {
                card.root.SetActive(card.spell.IsAvailable(caster.Loadout));
                card.keys.text=caster.Loadout.KeysFor(card.spell.Recipe).Replace(" → ","");
            }
            built=true;
        }
        status.text=$"Здоровье  {health.CurrentHealth} / {health.MaxHealth}"+(health.Shield>0?$"   Щит  {health.Shield}":"");
        if (healthFill != null) healthFill.fillAmount = Mathf.Clamp01((float)health.CurrentHealth / Mathf.Max(1, health.MaxHealth));
        combo.text=health.IsDead?"Возрождение…":caster.LoadoutReady?
            $"Q · {ElementLoadout.Label(caster.Loadout.q)}    E · {ElementLoadout.Label(caster.Loadout.e)}    R · {ElementLoadout.Label(caster.Loadout.r)}\n"+
            "Комбинация: "+caster.Loadout.KeysFor(caster.GetComponent<InputComboTracker>().History):"Подготовка стихий…";
        foreach(var card in cards)
        {
            float remaining=(float)caster.RemainingCooldown(card.spell);
            card.cover.fillAmount=Mathf.Clamp01(remaining/card.spell.Cooldown);
            card.seconds.text=remaining>0?Mathf.CeilToInt(remaining).ToString():"";
            card.icon.color=SpellIconGraphic.Tint(card.spell)*(remaining>0?.65f:1);
        }
        if(scoreboard.activeSelf&&Time.unscaledTime>=nextScore){nextScore=Time.unscaledTime+.25f;RefreshScoreboard();}
    }
    public void SetPause(bool open)
    {
        InputBlocked=open;if(pause!=null)pause.SetActive(open);
        Cursor.lockState=open?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=open;
    }
    public void RefreshScoreboard()
    {
        var players=FindObjectsByType<PlayerStats>(FindObjectsSortMode.None).OrderByDescending(p=>p.Kills).ThenBy(p=>p.Deaths).ThenBy(p=>p.netId);
        var ordered=players.ToList();
        table.text=string.Join("\n",ordered.Select(p=>p.DisplayName+(p.isLocalPlayer?"  (ты)":"")));
        killColumn.text=string.Join("\n",ordered.Select(p=>p.Kills));
        deathColumn.text=string.Join("\n",ordered.Select(p=>p.Deaths));
        pingColumn.text=string.Join("\n",ordered.Select(p=>p.Ping+" мс"));
        float height=Mathf.Max(372,ordered.Count*48);
        scoreContent.sizeDelta=new Vector2(0,height);
        foreach(var column in new[]{table,killColumn,deathColumn,pingColumn})column.rectTransform.sizeDelta=new Vector2(column.rectTransform.sizeDelta.x,height);
    }
    public void ReturnToMenu()
    {
        SetPause(false);
        var manager=NetworkManager.singleton;
        if(NetworkServer.active&&NetworkClient.active)manager.StopHost();
        else if(NetworkClient.active)manager.StopClient();
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying=false;
#else
        Application.Quit();
#endif
    }
#if UNITY_EDITOR
    // Run once by the scene migration. Gameplay only updates these saved objects.
    public void EditorBake(SpellManager catalog, Sprite cooldownSprite)
    {
        font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");fillSprite=cooldownSprite;
        var root=new GameObject("Player HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        root.transform.SetParent(transform,false);
        canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=150;
        var scale=root.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scale.referenceResolution=new Vector2(1280,720);scale.matchWidthOrHeight=.5f;
        status=Label(root.transform,"",24,0,1,24,-60,650,38);
        Label(root.transform,"ESC — меню   ·   TAB — игроки",20,1,1,-390,-52,370,32);
        var cross=Label(root.transform,"+",32,.5f,.5f,-18,-18,36,36);cross.alignment=TextAnchor.MiddleCenter;
        combo=Label(root.transform,"",18,.5f,0,-550,90,1100,48);combo.alignment=TextAnchor.MiddleCenter;
        pause=Shade(root.transform,"Pause");
        var panel=Panel(pause.transform,"Pause panel",.5f,.5f,-240,-190,480,380);
        Label(panel.transform,"МЕНЮ МАТЧА",30,0,1,26,-68,420,50);
        resumeButton=Button(panel.transform,"Продолжить",26,218,428,48);
        menuButton=Button(panel.transform,"В главное меню",26,152,428,48);
        quitButton=Button(panel.transform,"Выйти из игры",26,86,428,48);
        Label(panel.transform,"Онлайн-матч продолжается",20,0,0,26,18,428,35).alignment=TextAnchor.MiddleCenter;
        scoreboard=Shade(root.transform,"Scoreboard");
        var score=Panel(scoreboard.transform,"Lobby",.5f,.5f,-560,-270,1120,540);
        Label(score.transform,"ИГРОКИ МАТЧА",30,0,1,28,-65,1060,45);
        Label(score.transform,"Имя",22,0,1,28,-118,480,40);
        Label(score.transform,"Убийства",22,0,1,550,-118,170,40);
        Label(score.transform,"Смерти",22,0,1,750,-118,150,40);
        Label(score.transform,"Пинг",22,0,1,950,-118,150,40);
        var viewport=Panel(score.transform,"Players",0,1,28,-500,1064,372);viewport.AddComponent<RectMask2D>();
        var content=new GameObject("Rows",typeof(RectTransform));content.transform.SetParent(viewport.transform,false);
        scoreContent=content.GetComponent<RectTransform>();scoreContent.anchorMin=new Vector2(0,1);scoreContent.anchorMax=Vector2.one;scoreContent.pivot=new Vector2(0,1);scoreContent.sizeDelta=new Vector2(0,372);
        var scroll=viewport.AddComponent<ScrollRect>();scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=scoreContent;scroll.horizontal=false;scroll.scrollSensitivity=30;scroll.movementType=ScrollRect.MovementType.Clamped;
        table=Label(content.transform,"",24,0,1,0,-372,480,372);
        killColumn=Label(content.transform,"",24,0,1,522,-372,170,372);
        deathColumn=Label(content.transform,"",24,0,1,722,-372,150,372);
        pingColumn=Label(content.transform,"",24,0,1,922,-372,140,372);
        foreach(var column in new[]{table,killColumn,deathColumn,pingColumn}){column.rectTransform.pivot=new Vector2(0,1);column.rectTransform.anchoredPosition=new Vector2(column.rectTransform.anchoredPosition.x,0);column.lineSpacing=1.6f;}
        scoreboard.SetActive(false);
        pause.SetActive(false);
        BuildCards(catalog);
    }
    private void BuildCards(SpellManager catalog)
    {
        var available=catalog.Spells.Where(s=>s!=null).ToList();
        float width=54;
        var row=new GameObject("Cooldown bar",typeof(RectTransform),typeof(HorizontalLayoutGroup));
        row.transform.SetParent(canvas.transform,false);Place(row,.5f,0,-610,18,1220,64);
        var layout=row.GetComponent<HorizontalLayoutGroup>();layout.spacing=6;layout.childAlignment=TextAnchor.MiddleCenter;
        layout.childControlWidth=false;layout.childControlHeight=false;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
        for(int i=0;i<available.Count;i++)
        {
            var spell=available[i];float x=-width*available.Count*.5f+i*width;
            var panel=Panel(row.transform,spell.name,0,0,0,0,width-6,64);
            var title=Label(panel.transform,spell.Name,18,0,1,4,-48,width-14,46);title.alignment=TextAnchor.MiddleCenter;title.gameObject.SetActive(false);
            var iconObject=new GameObject("Spell icon",typeof(RectTransform),typeof(SpellIconGraphic));
            iconObject.transform.SetParent(panel.transform,false);Place(iconObject,.5f,0,-32f/3,26,64f/3,64f/3);
            var icon=iconObject.GetComponent<SpellIconGraphic>();icon.spell=spell;icon.color=SpellIconGraphic.Tint(spell);icon.raycastTarget=false;
            var cover=Panel(iconObject.transform,"Cooldown",0,0,0,0,64f/3,64f/3).GetComponent<Image>();
            cover.color=new Color(0,0,0,.72f);cover.type=Image.Type.Filled;cover.fillMethod=Image.FillMethod.Vertical;cover.fillOrigin=0;
            cover.sprite=fillSprite;
            var seconds=Label(iconObject.transform,"",14,0,0,0,0,64f/3,64f/3);seconds.alignment=TextAnchor.MiddleCenter;seconds.fontStyle=FontStyle.Bold;
            var keys=Label(panel.transform,ElementLoadout.Default.KeysFor(spell.Recipe).Replace(" → ",""),14,0,0,2,2,width-10,20);keys.alignment=TextAnchor.MiddleCenter;
            cover.fillAmount=0;
            cards.Add(new Card{spell=spell,root=panel,keys=keys,icon=icon,cover=cover,seconds=seconds});
            panel.SetActive(spell.IsAvailable(ElementLoadout.Default));
        }
        // Menus must render over cards, which are created after the initial canvas.
        pause.transform.SetAsLastSibling();scoreboard.transform.SetAsLastSibling();
    }
    private GameObject Shade(Transform parent,string name)
    {
        var go=Panel(parent,name,0,0,0,0,0,0);
        var r=go.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        go.GetComponent<Image>().color=new Color(.015f,.025f,.05f,.94f);return go;
    }
    private GameObject Panel(Transform parent,string name,float ax,float ay,float x,float y,float w,float h)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
        Place(go,ax,ay,x,y,w,h);go.GetComponent<Image>().color=new Color(.08f,.11f,.18f,.95f);return go;
    }
    private Text Label(Transform parent,string text,int size,float ax,float ay,float x,float y,float w,float h)
    {
        var go=new GameObject("Label",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);Place(go,ax,ay,x,y,w,h);
        var label=go.GetComponent<Text>();label.font=font;label.fontSize=size;label.color=new Color(.9f,.94f,1);label.text=text;
        label.raycastTarget=false;label.supportRichText=false;return label;
    }
    private Button Button(Transform parent,string text,float x,float y,float w,float h)
    {
        var go=Panel(parent,text,0,0,x,y,w,h);go.GetComponent<Image>().color=new Color(.16f,.25f,.38f);
        var button=go.AddComponent<Button>();Label(go.transform,text,24,0,0,0,0,w,h).alignment=TextAnchor.MiddleCenter;return button;
    }
    private static void Place(GameObject go,float ax,float ay,float x,float y,float w,float h)
    {
        var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(ax,ay);r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);
    }
#endif
    private void OnDisable(){InputBlocked=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
}
