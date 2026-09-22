using Mirror;
using UnityEngine;
using UnityEngine.UI;

// позволяет до подключения выбрать стихии и просмотреть доступные рецепты в книге заклинаний.
public class ElementLoadoutUI : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private GameObject screen;
    [SerializeField] private Transform cards;
    [SerializeField] private Text heading;
    [SerializeField] private Text[] slotLabels = new Text[3];
    [SerializeField] private Button[] slotButtons = new Button[3];
    [SerializeField] private Button opener, close, filter;
    [SerializeField] private Button[] elementButtons = new Button[5];
    // связывает заклинание с фоном и текстом состояния его карточки в книге.
    [System.Serializable]
    private class SpellCard { public Spell spell; public Image background; public Text status; }
    [SerializeField] private System.Collections.Generic.List<SpellCard> spellCards = new System.Collections.Generic.List<SpellCard>();
    private Font font;
    private int selectedSlot;
    private bool onlyAvailable = true;
    private Color ink = new Color(0.86f, 0.9f, 0.97f);
    private Color panel = new Color(0.07f, 0.09f, 0.15f, 1);
    private readonly string[] names = { "Огонь", "Воздух", "Лёд", "Земля", "Вода" };
    // подключаем кнопки выбора слотов, стихий и фильтра к уже сохранённому интерфейсу.
    private void Start()
    {
        spellCards.Sort((a,b)=>Spell.CompareSimplicity(a.spell,b.spell));
        foreach(var card in spellCards)card.background.transform.SetAsLastSibling();
        opener.onClick.AddListener(()=>{screen.SetActive(true);Refresh();});
        close.onClick.AddListener(()=>screen.SetActive(false));
        filter.onClick.AddListener(()=>{onlyAvailable=!onlyAvailable;Refresh();});
        for(int i=0;i<slotButtons.Length;i++){int slot=i;slotButtons[i].onClick.AddListener(()=>{selectedSlot=slot;Refresh();});}
        for(int i=0;i<elementButtons.Length;i++){int element=i;elementButtons[i].onClick.AddListener(()=>{LocalPlayerSettings.Instance.SetElement(selectedSlot,(MagicElement)element);Refresh();});}
        screen.SetActive(false);Refresh();
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    // скрываем книгу после запуска сети; до матча разрешаем закрывать её клавишей Escape.
    private void Update()
    {
        if (canvas == null) return;
        canvas.enabled = !NetworkClient.active && !NetworkServer.active;
        if (canvas.enabled && screen.activeSelf && Input.GetKeyDown(KeyCode.Escape)) screen.SetActive(false);
    }
#if UNITY_EDITOR
    // создаём иерархию книги и карточки в редакторе для последующего сохранения в префаб.
    public void EditorBake(SpellManager catalog)
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var root = new GameObject("Spellbook UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
        opener = Button(root.transform, "Стихии и заклинания", () => { screen.SetActive(true); Refresh(); });
        Rect(opener.gameObject, 1, 1, new Vector2(-290, -60), new Vector2(270, 42));
        screen = Panel(root.transform, "Spellbook screen", new Color(0.025f, 0.035f, 0.07f, 0.98f));
        Stretch(screen.GetComponent<RectTransform>());
        var content = Panel(screen.transform, "Spellbook", panel);
        Rect(content, 0.5f, 0.5f, new Vector2(-600, -325), new Vector2(1200, 650));
        TextAt(content.transform, "КНИГА СТИХИЙ", 28, new Vector2(26, -18), new Vector2(850, 40));
        TextAt(content.transform, "Три стихии. Комбинации из трёх нажатий. Порядок важен.", 20,
            new Vector2(26, -62), new Vector2(920, 28));
        close = Button(content.transform, "Закрыть  ×", () => screen.SetActive(false));
        Rect(close.gameObject, 0, 1, new Vector2(1020, -65), new Vector2(154, 40));
        var left = Panel(content.transform, "Element selection", new Color(0.10f, 0.13f, 0.21f));
        Rect(left, 0, 1, new Vector2(24, -620), new Vector2(300, 510));
        TextAt(left.transform, "ТВОЙ НАБОР", 20, new Vector2(18, -16), new Vector2(260, 28));
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            var b = Button(left.transform, "", () => { selectedSlot = slot; Refresh(); });
            Rect(b.gameObject, 0, 1, new Vector2(18, -98 - i * 53), new Vector2(264, 44));
            slotButtons[i] = b; slotLabels[i] = b.GetComponentInChildren<Text>();
        }
        TextAt(left.transform, "Выбери слот выше, затем стихию:", 18, new Vector2(18, -220), new Vector2(264, 42));
        for (int i = 0; i < names.Length; i++)
        {
            int element = i;
            var b = Button(left.transform, names[i], () =>
            {
                LocalPlayerSettings.Instance.SetElement(selectedSlot, (MagicElement)element);
                Refresh();
            });
            elementButtons[i]=b;
            Rect(b.gameObject, 0, 1, new Vector2(18 + (i % 2) * 135, -306 - (i / 2) * 46), new Vector2(129, 38));
        }
        TextAt(left.transform, "Занятые стихии меняются местами.\nНабор сохраняется.", 18,
            new Vector2(18, -422), new Vector2(264, 70));
        heading = TextAt(content.transform, "", 20, new Vector2(346, -112), new Vector2(600, 36));
        filter = Button(content.transform, "Все / доступные", () => { onlyAvailable = !onlyAvailable; Refresh(); });
        Rect(filter.gameObject, 0, 1, new Vector2(986, -152), new Vector2(188, 36));
        var viewport = Panel(content.transform, "Spell list", new Color(0.055f, 0.07f, 0.12f));
        Rect(viewport, 0, 1, new Vector2(346, -620), new Vector2(828, 458));
        viewport.AddComponent<RectMask2D>();
        var scroll = viewport.AddComponent<ScrollRect>();
        var list = new GameObject("Cards", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        list.transform.SetParent(viewport.transform, false);
        var rect = list.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = Vector2.zero;
        var layout = list.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8; layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlHeight = true; layout.childForceExpandHeight = false;
        layout.childControlWidth = true; layout.childForceExpandWidth = true;
        list.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = rect; scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
        cards = list.transform;
        EditorBakeCards(catalog);
        screen.SetActive(false);
    }
#endif
    // обновляем назначения клавиш, доступность карточек и счётчик под текущий набор стихий.
    private void Refresh()
    {
        var settings = LocalPlayerSettings.Instance;
        if (settings == null) return;
        for (int i = 0; i < 3; i++)
        {
            slotLabels[i].text = (i == 0 ? "Q" : i == 1 ? "E" : "R") + "   ·   " + ElementLoadout.Label(settings.Loadout.Get(i));
            slotButtons[i].GetComponent<Image>().color = i == selectedSlot ? new Color(0.18f, 0.34f, 0.52f) : new Color(0.14f, 0.18f, 0.27f);
        }
        int available = 0;
        foreach (var card in spellCards)
        {
            bool unlocked=card.spell.IsAvailable(settings.Loadout);
            if(unlocked)available++;
            card.background.gameObject.SetActive(!onlyAvailable||unlocked);
            var icon=card.background.GetComponentInChildren<SpellIconGraphic>(true);
            icon.spell=card.spell;
            icon.SetAllDirty();
            card.background.color=unlocked?new Color(.11f,.18f,.25f):new Color(.09f,.11f,.16f);
            card.status.text=unlocked?settings.Loadout.KeysFor(card.spell.Recipe)+"\n"+card.spell.Cooldown.ToString("0.#")+" с":"Нужны другие\nстихии";
            card.status.color=unlocked?new Color(.55f,.9f,.8f):new Color(.58f,.62f,.7f);
        }
        heading.text=$"ДОСТУПНО {available} / {spellCards.Count}"+(onlyAvailable?"   ·   выбранный набор":"   ·   все заклинания");
    }
#if UNITY_EDITOR
    // заранее создаём карточки каталога с названиями, рецептами, описаниями и перезарядкой.
    private void EditorBakeCards(SpellManager catalog)
    {
        foreach (Spell spell in catalog.Spells)
        {
            if (spell == null) continue;
            bool unlocked = spell.IsAvailable(ElementLoadout.Default);
            var card = Panel(cards, spell.Name, unlocked ? new Color(0.11f, 0.18f, 0.25f) : new Color(0.09f, 0.11f, 0.16f));
            card.AddComponent<LayoutElement>().preferredHeight = 150;
            var iconObject=new GameObject("Spell icon",typeof(RectTransform),typeof(SpellIconGraphic));
            iconObject.transform.SetParent(card.transform,false);Rect(iconObject,0,1,new Vector2(12,-91),new Vector2(64,64));
            var icon=iconObject.GetComponent<SpellIconGraphic>();icon.spell=spell;icon.color=SpellIconGraphic.Tint(spell);icon.raycastTarget=false;
            TextAt(card.transform, spell.Name, 26, new Vector2(88, -8), new Vector2(490, 36));
            var recipeNames = new string[spell.Recipe.Count];
            for (int i = 0; i < recipeNames.Length; i++) recipeNames[i] = ElementLoadout.Label(spell.Recipe[i]);
            TextAt(card.transform, string.Join(" → ", recipeNames), 18, new Vector2(88, -46), new Vector2(490, 28));
            TextAt(card.transform, spell.Description, 18, new Vector2(88, -80), new Vector2(475, 64));
            var status = TextAt(card.transform, unlocked ? ElementLoadout.Default.KeysFor(spell.Recipe) + "\n" + spell.Cooldown.ToString("0.#") + " с" :
                "Нужны другие\nстихии", 22, new Vector2(580, -34), new Vector2(190, 76));
            status.alignment = TextAnchor.MiddleRight;
            status.color = unlocked ? new Color(0.55f, 0.9f, 0.8f) : new Color(0.58f, 0.62f, 0.7f);
            spellCards.Add(new SpellCard{spell=spell,background=card.GetComponent<Image>(),status=status});
        }
        for(int i=0;i<3;i++)slotLabels[i].text=(i==0?"Q":i==1?"E":"R")+" · "+ElementLoadout.Label(ElementLoadout.Default.Get(i));
        heading.text="КАТАЛОГ ЗАКЛИНАНИЙ";
    }
    // создаём дочернюю панель с заданным цветом фона.
    private GameObject Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false); go.GetComponent<Image>().color = color; return go;
    }
    // создаём кнопку и подпись; обработчики подключаются в Start к сохранённым ссылкам.
    private Button Button(Transform parent, string label, UnityEngine.Events.UnityAction click)
    {
        var go = Panel(parent, label, new Color(0.14f, 0.2f, 0.3f));
        var b = go.AddComponent<Button>();
        var text = TextAt(go.transform, label, 21, Vector2.zero, Vector2.zero);
        Stretch(text.rectTransform); text.alignment = TextAnchor.MiddleCenter;
        return b;
    }
    // создаём текст с отступом от верхнего края и заданными размерами.
    private Text TextAt(Transform parent, string value, int size, Vector2 offset, Vector2 dimensions)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>(); t.font = font; t.fontSize = size; t.color = ink; t.text = value;
        t.raycastTarget = false; t.verticalOverflow = VerticalWrapMode.Truncate;
        Rect(go, 0, 1, new Vector2(offset.x, offset.y - dimensions.y), dimensions);
        return t;
    }
    // задаём якорь, нижний левый угол и размеры прямоугольника интерфейса.
    private static void Rect(GameObject go, float x, float y, Vector2 offset, Vector2 dimensions)
    {
        var r = go.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(x, y);
        r.pivot = Vector2.zero; r.anchoredPosition = offset; r.sizeDelta = dimensions;
    }
    // растягиваем элемент на всю площадь родителя без отступов.
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
#endif
}
