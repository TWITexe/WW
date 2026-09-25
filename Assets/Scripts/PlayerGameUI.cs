using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

// связывает сохранённый интерфейс матча с локальным игроком: здоровье, комбинации, перезарядки и таблицу.
[DefaultExecutionOrder(-100)]
public partial class PlayerGameUI : MonoBehaviour
{
    public static bool InputBlocked { get; private set; }
    private PlayerNetworkCaster caster;
    private SpellManager spells;
    private Health health;
    private RelativeMovement movement;
    private InputComboTracker comboTracker;
    [SerializeField] private Canvas canvas;
    [SerializeField] private GameObject pause, scoreboard;
    [SerializeField] private Text status, combo, table;
    [SerializeField] private Image healthFill;
    [SerializeField] private Image staminaFill;
    [SerializeField] private Text staminaLabel;
    private int displayedStamina = -1;
    [SerializeField] private Text killColumn, deathColumn, pingColumn;
    [SerializeField] private RectTransform scoreContent;
    [SerializeField] private Button resumeButton, menuButton, quitButton;
    private Sprite fillSprite;
    private Font font;
    private bool built;
    [SerializeField] private UltimateHUD ultimateHUD;
    private PlayerUltimate ultimate;
    private float nextScore;
    private int displayedHealth = -1, displayedShield = -1, displayedMaxHealth = -1;
    private string loadoutCaption;
    private int displayedCombo = int.MinValue;
    [SerializeField] private Text killFeed;
    private float nextKillFeed;
    [SerializeField] private List<Card> cards=new List<Card>();
    // объединяет ссылки на элементы одной карточки заклинания в интерфейсе матча.
    [System.Serializable]
    private class Card
    {
        public Spell spell;
        public GameObject root;
        public Text keys;
        public Image cover;
        public Text seconds;
        public SpellIconGraphic icon;
        [System.NonSerialized] public int displayedSeconds = -1;
    }
    // скрываем интерфейс до появления игрока и подключаем кнопки меню.
    private void Start()
    {
        canvas.enabled=false;
        MatchKillFeed.Clear();
        cards.Sort((a,b)=>Spell.CompareSimplicity(a.spell,b.spell));
        foreach(var card in cards)card.root.transform.SetAsLastSibling();
        pause.SetActive(false);scoreboard.SetActive(false);
        resumeButton.onClick.AddListener(()=>SetPause(false));
        menuButton.onClick.AddListener(ReturnToMenu);
        quitButton.onClick.AddListener(QuitGame);
        BindMatchUI();
    }
    // ожидаем локального игрока, фильтруем его заклинания и обновляем здоровье, комбинацию и перезарядки.
    private void Update()
    {
        // интерфейс сцены может запуститься раньше сетевого игрока, поэтому ждём его появления.
        if(caster==null)
        {
            if(NetworkClient.localPlayer==null)return;
            caster=NetworkClient.localPlayer.GetComponentInChildren<PlayerNetworkCaster>();
            if(caster==null)return;
            spells=caster.GetComponent<SpellManager>();health=caster.GetComponent<Health>();
            comboTracker = caster.GetComponent<InputComboTracker>();
            movement = caster.GetComponent<RelativeMovement>();
            ultimate = caster.GetComponent<PlayerUltimate>();
            displayedStamina = -1;
            ResetVitals();
            canvas.enabled=true;built=false;SetPause(false);
        }
        if (UpdateMatchUI()) return;
        if(Input.GetKeyDown(KeyCode.Escape))SetPause(!InputBlocked);
        scoreboard.SetActive(!InputBlocked&&Input.GetKey(KeyCode.Tab));
        if(caster.LoadoutReady&&!built)
        {
            // набор фиксируется на матч: доступность карточек достаточно настроить один раз.
            foreach(var card in cards)
            {
                card.root.SetActive(card.spell.IsAvailable(caster.Loadout));
                card.keys.text=caster.Loadout.KeysFor(card.spell.Recipe).Replace(" → ","");
            }
            built=true;
            if (ultimateHUD != null)
            {
                ultimateHUD.Bind(ultimate);
                int visible = 0;
                foreach (var card in cards)
                {
                    if (!card.root.activeSelf) continue;
                    if (visible++ == 4) ultimateHUD.transform.SetAsLastSibling();
                    card.root.transform.SetAsLastSibling();
                }
            }
            loadoutCaption = $"Q · {ElementLoadout.Label(caster.Loadout.q)}    E · {ElementLoadout.Label(caster.Loadout.e)}    R · {ElementLoadout.Label(caster.Loadout.r)}\nКомбинация: ";
            displayedCombo = int.MinValue;
        }
        // текст создаём только при изменении здоровья, а не выделяем одинаковую строку каждый кадр.
        if (displayedHealth != health.CurrentHealth || displayedShield != health.Shield || displayedMaxHealth != health.MaxHealth)
        {
            displayedHealth = health.CurrentHealth;
            displayedShield = health.Shield;
            displayedMaxHealth = health.MaxHealth;
            if (vitalsHealthText != null)
            {
                vitalsHealthText.SetText("{0}", displayedHealth);
                vitalsMaxHealthText.SetText("/ {0}", displayedMaxHealth);
                vitalsShieldText.SetText("{0}", displayedShield);
            }
            else if (status != null)
                status.text=$"Здоровье  {displayedHealth} / {displayedMaxHealth}"+(displayedShield>0?$"   Щит  {displayedShield}":"");
        }
        if (healthFill != null) healthFill.fillAmount = Mathf.Clamp01((float)health.CurrentHealth / Mathf.Max(1, health.MaxHealth));
        UpdateShieldBar();
        // показываем предсказанный запас владельца, который сверяется с сервером вместе с позицией.
        if (movement != null && staminaFill != null)
        {
            float fraction = health.IsDead ? 0 : Mathf.Clamp01(movement.Stamina / Mathf.Max(1, movement.MaxStamina));
            staminaFill.rectTransform.anchorMax = new Vector2(fraction, 1);
            staminaFill.color = new Color(.2f,.55f,1f);
            if (vitalsStaminaText != null) vitalsStaminaText.color = staminaFill.color;
            int value = Mathf.CeilToInt(fraction * movement.MaxStamina);
            if (value != displayedStamina)
            {
                displayedStamina = value;
                if (vitalsStaminaText != null) vitalsStaminaText.SetText("{0}", value);
                else if (staminaLabel != null) staminaLabel.text = $"Стамина  {value} / {Mathf.RoundToInt(movement.MaxStamina)}";
            }
        }
        // код последовательности позволяет заметить изменение без создания строк и массивов при неподвижном вводе.
        int comboCode = 1;
        for (int index = 0; index < comboTracker.History.Count; index++)
            comboCode = comboCode * 6 + (int)comboTracker.History[index] + 1;
        if (health.IsDead) comboCode = -1;
        else if (!caster.LoadoutReady) comboCode = -2;
        if (displayedCombo != comboCode)
        {
            displayedCombo = comboCode;
            combo.text = health.IsDead ? "Возрождение…" : caster.LoadoutReady ?
                loadoutCaption + caster.Loadout.KeysFor(comboTracker.History) : "Подготовка стихий…";
        }
        string areaCaption=caster.AreaAimCaption;
        if (ultimate != null && ultimate.Caption != null) areaCaption = ultimate.Caption;
        if(areaCaption != null) { combo.text = areaCaption; displayedCombo=int.MinValue; }
        if(killFeed!=null && Time.unscaledTime>=nextKillFeed)
        {
            nextKillFeed=Time.unscaledTime+.1f;
            killFeed.text=MatchKillFeed.Read();
        }
        foreach(var card in cards)
        {
            if (!card.root.activeSelf) continue;
            float remaining=(float)caster.RemainingCooldown(card.spell);
            // затемнение уменьшается плавно, а число секунд округляется вверх до полной готовности.
            card.cover.fillAmount=Mathf.Clamp01(remaining/card.spell.Cooldown);
            int secondsLeft = Mathf.CeilToInt(remaining);
            if (card.displayedSeconds != secondsLeft)
            {
                card.displayedSeconds = secondsLeft;
                card.seconds.text = secondsLeft > 0 ? secondsLeft.ToString() : "";
            }
            // всю карточку затемняет маска: иконка под ней сохраняет свой цвет без скачка при готовности.
            card.icon.color=SpellIconGraphic.Tint(card.spell);
        }
        // обновляем открытую таблицу четыре раза в секунду, а не пересобираем строки каждый кадр.
        if(scoreboard.activeSelf&&Time.unscaledTime>=nextScore){nextScore=Time.unscaledTime+.25f;RefreshScoreboard();}
    }
    // блокируем локальный ввод и освобождаем курсор; сетевой матч при этом продолжается.
    public void SetPause(bool open)
    {
        if (resultsPanel != null && resultsPanel.activeSelf) return;
        InputBlocked=open;if(pause!=null)pause.SetActive(open);
        Cursor.lockState=open?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=open;
    }
    // сортируем игроков по убийствам и смертям, затем синхронно заполняем все столбцы таблицы.
    public void RefreshScoreboard()
    {
        var players=PlayerStats.ClientPlayers.OrderByDescending(p=>p.Kills).ThenBy(p=>p.Deaths).ThenBy(p=>p.netId);
        var ordered=players.ToList();
        table.supportRichText=true;
        table.text=string.Join("\n",ordered.Select(p=>MatchKillFeed.ColoredName("● ",p.DisplayColor)+p.DisplayName.Replace("<", "").Replace(">", "")+(p.isLocalPlayer?"  (ты)":"")));
        killColumn.text=string.Join("\n",ordered.Select(p=>p.Kills));
        deathColumn.text=string.Join("\n",ordered.Select(p=>p.Deaths));
        pingColumn.text=string.Join("\n",ordered.Select(p=>p.Ping+" мс"));
        float height=Mathf.Max(372,ordered.Count*48);
        scoreContent.sizeDelta=new Vector2(0,height);
        foreach(var column in new[]{table,killColumn,deathColumn,pingColumn})column.rectTransform.sizeDelta=new Vector2(column.rectTransform.sizeDelta.x,height);
    }
    // завершаем локальный хост или клиент и возвращаем свободный курсор.
    public void ReturnToMenu()
    {
        if (NetManager.Room != null) { NetManager.Room.LeaveRoom(); return; }
        SetPause(false);
        var manager=NetworkManager.singleton;
        if(NetworkServer.active&&NetworkClient.active)manager.StopHost();
        else if(NetworkClient.active)manager.StopClient();
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    // в редакторе останавливаем игровой режим, в сборке закрываем приложение.
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying=false;
#else
        Application.Quit();
#endif
    }
#if UNITY_EDITOR
    // создаём интерфейс при подготовке сцены; во время матча обновляем уже сохранённые объекты.
    // создаём сохраняемую иерархию интерфейса в редакторе; во время матча этот метод недоступен.
    public void EditorBake(SpellManager catalog, Sprite cooldownSprite)
    {
        font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");fillSprite=cooldownSprite;
        var root=new GameObject("Player HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        root.transform.SetParent(transform,false);
        canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=150;
        var scale=root.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scale.referenceResolution=new Vector2(1280,720);scale.matchWidthOrHeight=.5f;
        // сохраняем полоску справа над нижним рядом заклинаний.
        var staminaPanel=Panel(root.transform,"Stamina",1,0,-260,104,236,18);
        staminaPanel.GetComponent<Image>().raycastTarget=false;
        var staminaBar=Panel(staminaPanel.transform,"Stamina fill",0,0,0,0,0,0);
        staminaFill=staminaBar.GetComponent<Image>();staminaFill.raycastTarget=false;
        var staminaRect=staminaFill.rectTransform;staminaRect.anchorMax=Vector2.one;
        staminaRect.offsetMin=staminaRect.offsetMax=Vector2.zero;
        staminaLabel=Label(root.transform,"Стамина  100 / 100",18,1,0,-260,126,236,28);
        staminaLabel.alignment=TextAnchor.MiddleRight;
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
    // создаём карточки всего каталога, чтобы в матче оставалось только скрыть недоступные.
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
        EditorStyleCooldowns();
        // переносим меню поверх карточек, добавленных после первоначального создания холста.
        pause.transform.SetAsLastSibling();scoreboard.transform.SetAsLastSibling();
    }
    // сохраняем оформление в префабе: маска покрывает всю карточку, число остаётся поверх затемнения.
    public void EditorStyleCooldowns()
    {
        foreach (var card in cards)
        {
            var cover = card.cover;
            cover.transform.SetParent(card.root.transform, false);
            cover.rectTransform.anchorMin = Vector2.zero;
            cover.rectTransform.anchorMax = Vector2.one;
            cover.rectTransform.offsetMin = cover.rectTransform.offsetMax = Vector2.zero;
            cover.color = new Color(0, 0, 0, .72f);
            cover.type = Image.Type.Filled;
            cover.fillMethod = Image.FillMethod.Vertical;
            cover.fillOrigin = 0;
            cover.raycastTarget = false;
            cover.transform.SetAsLastSibling();

            var seconds = card.seconds;
            seconds.transform.SetParent(card.root.transform, false);
            seconds.rectTransform.anchorMin = Vector2.zero;
            seconds.rectTransform.anchorMax = Vector2.one;
            seconds.rectTransform.offsetMin = new Vector2(1, 14);
            seconds.rectTransform.offsetMax = new Vector2(-1, -1);
            seconds.fontSize = 38;
            seconds.resizeTextForBestFit = true;
            seconds.resizeTextMinSize = 24;
            seconds.resizeTextMaxSize = 42;
            seconds.fontStyle = FontStyle.Normal;
            seconds.color = new Color(1, 1, 1, .75f);
            seconds.alignment = TextAnchor.MiddleCenter;
            seconds.raycastTarget = false;
            seconds.transform.SetAsLastSibling();
        }
    }
    // создаём растянутую затемняющую подложку для меню или таблицы игроков.
    private GameObject Shade(Transform parent,string name)
    {
        var go=Panel(parent,name,0,0,0,0,0,0);
        var r=go.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        go.GetComponent<Image>().color=new Color(.015f,.025f,.05f,.94f);return go;
    }
    // создаём прямоугольную панель заданного размера и положения.
    private GameObject Panel(Transform parent,string name,float ax,float ay,float x,float y,float w,float h)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
        Place(go,ax,ay,x,y,w,h);go.GetComponent<Image>().color=new Color(.08f,.11f,.18f,.95f);return go;
    }
    // создаём текстовую подпись, которая не перехватывает нажатия мыши.
    private Text Label(Transform parent,string text,int size,float ax,float ay,float x,float y,float w,float h)
    {
        var go=new GameObject("Label",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);Place(go,ax,ay,x,y,w,h);
        var label=go.GetComponent<Text>();label.font=font;label.fontSize=size;label.color=new Color(.9f,.94f,1);label.text=text;
        label.raycastTarget=false;label.supportRichText=false;return label;
    }
    // создаём кнопку с подписью; обработчик назначается отдельно при запуске интерфейса.
    private Button Button(Transform parent,string text,float x,float y,float w,float h)
    {
        var go=Panel(parent,text,0,0,x,y,w,h);go.GetComponent<Image>().color=new Color(.16f,.25f,.38f);
        var button=go.AddComponent<Button>();Label(go.transform,text,24,0,0,0,0,w,h).alignment=TextAnchor.MiddleCenter;return button;
    }
    // задаём якорь, положение и размер элемента относительно родителя.
    private static void Place(GameObject go,float ax,float ay,float x,float y,float w,float h)
    {
        var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(ax,ay);r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);
    }
#endif
    // снимаем блокировку ввода и освобождаем курсор при отключении интерфейса.
    private void OnDisable(){Canvas.willRenderCanvases-=PositionVitals;InputBlocked=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
}
