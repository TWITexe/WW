using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// обновляет сохранённый каталог на поверхности полок без создания экранного overlay-интерфейса.
public class ShelfSpellCatalogUI : MonoBehaviour
{
    [SerializeField] private Text heading;
    [SerializeField] private Text hint;
    [SerializeField] private Text[] slotLabels;
    [SerializeField] private Button[] slotButtons;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private List<SpellCard> cards = new List<SpellCard>();
    [SerializeField] private SavedElementBuildUI savedBuilds;
    private bool catalogReady;
    private ElementLoadout displayedLoadout;
    private UnityEngine.UI.GraphicRaycaster inputRaycaster;

    // видимый каталог в общем ракурсе не должен перехватывать указатель у полки.
    private void Awake()
    {
        cards.Sort((a,b)=>Spell.CompareSimplicity(a.spell,b.spell));
        foreach(var card in cards)card.root.transform.SetAsLastSibling();
        SetInteraction(false);
    }

    // разрешаем ввод только в ракурсе полок, не меняя видимость карточек.
    public void SetInteraction(bool enabled)
    {
        if (inputRaycaster == null) inputRaycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
        if (inputRaycaster != null) inputRaycaster.enabled = enabled;
        if (!enabled && scroll != null) scroll.StopMovement();
        if (savedBuilds != null) savedBuilds.SetInteraction(enabled);
    }

    public bool BuildsHandleInput() => savedBuilds != null && savedBuilds.HandlesInput;

    // объединяет элементы одной сохранённой карточки заклинания.
    [System.Serializable]
    public class SpellCard
    {
        public Spell spell;
        public GameObject root;
        public Text recipe;
    }

    // подключаем выбор слота к мировым надписям; отдельные кнопки стихий заменены физическими книгами.
    public void Bind(InteractiveShelfMenu menu)
    {
        if (savedBuilds != null) savedBuilds.Bind(menu);
        for (int index = 0; index < slotButtons.Length; index++)
        {
            int slot = index;
            slotButtons[index].onClick.RemoveAllListeners();
            slotButtons[index].onClick.AddListener(() => menu.SelectSlot(slot));
        }
    }

    // показываем все доступные рецепты выбранного набора и текущий слот назначения.
    public void Refresh(ElementLoadout loadout, int selectedSlot)
    {
        if (savedBuilds != null) savedBuilds.Refresh(loadout);
        for (int slot = 0; slot < slotLabels.Length; slot++)
        {
            string key = slot == 0 ? "Q" : slot == 1 ? "E" : "R";
            slotLabels[slot].text = key + "  " + ElementLoadout.Label(loadout.Get(slot));
            slotLabels[slot].color = slot == selectedSlot ? new Color(1f, .82f, .4f) : new Color(.75f, .8f, .83f);
        }

        // смена выделенного слота не меняет рецепты и не должна сбрасывать прокрутку или перестраивать весь холст.
        if (catalogReady && displayedLoadout.q == loadout.q && displayedLoadout.e == loadout.e && displayedLoadout.r == loadout.r)
            return;
        catalogReady = true;
        displayedLoadout = loadout;

        int available = 0;
        foreach (SpellCard card in cards)
        {
            bool unlocked = card.spell != null && card.spell.IsAvailable(loadout);
            card.root.SetActive(unlocked);
            if (!unlocked) continue;
            available++;
            card.recipe.text = loadout.KeysFor(card.spell.Recipe) + "   ·   " + card.spell.Cooldown.ToString("0.#") + " с";
        }

        heading.text = "КНИГА ЗАКЛИНАНИЙ  ·  " + available + " / " + cards.Count;
        hint.text = "разворот / Q E R — слот · книга — стихия\nколесо — список · Esc / ПКМ — назад";
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1f;
    }

#if UNITY_EDITOR
    public void ConfigureBuilds(SavedElementBuildUI builds) => savedBuilds = builds;
    // отключаем прежние назначения справа после переноса управления на открытые книги.
    public void DetachSlotControls()
    {
        slotLabels = new Text[0];
        slotButtons = new Button[0];
    }

    // записываем созданные в редакторе элементы в сериализованные поля компонента.
    public void Configure(Text title, Text instruction, Text[] labels, Button[] buttons,
        ScrollRect list, List<SpellCard> spellCards)
    {
        heading = title;
        hint = instruction;
        slotLabels = labels;
        slotButtons = buttons;
        scroll = list;
        cards = spellCards;
    }
#endif
}
