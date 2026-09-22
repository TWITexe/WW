using UnityEngine;
using UnityEngine.UI;

// показывает клавишу на левой странице и назначенную стихию на правой странице открытой книги.
public class OpenBookSlot : MonoBehaviour
{
    [SerializeField, Range(0, 2)] private int slot;
    [SerializeField] private BoxCollider hitArea;
    [SerializeField] private GameObject pageContent;
    [SerializeField] private Text keyLabel;
    [SerializeField] private Image selectionMark;
    [SerializeField] private SpellIconGraphic elementIcon;
    [SerializeField] private Spell[] elementSymbols;
    [SerializeField] private BookSymbolGraphic bookIcon;
    [SerializeField] private Mesh[] bookSymbols;
    private bool appearanceReady;
    private MagicElement displayedElement;
    private bool displayedSelection;

    public int Slot => slot;
    public BoxCollider HitArea => hitArea;

    // проверяем только сохранённый коллайдер этой книги, без поиска объектов сцены.
    public bool Raycast(Ray ray, out RaycastHit hit) => hitArea.Raycast(ray, out hit, 30f);

    // скрываем надписи во время общего обзора и полёта камеры, оставляя саму модель на месте.
    public void SetVisible(bool visible) => pageContent.SetActive(visible);

    // меняем геометрию значка лишь при назначении другой стихии; постоянного обновления в кадре нет.
    public void Refresh(ElementLoadout loadout, int selectedSlot)
    {
        MagicElement element = loadout.Get(slot);
        bool selected = selectedSlot == slot;
        if (!appearanceReady || displayedElement != element)
        {
            if (bookIcon != null)
                bookIcon.SetSymbol(bookSymbols[(int)element]);
            else
            {
                elementIcon.spell = elementSymbols[(int)element];
                elementIcon.color = SpellIconGraphic.Tint(elementIcon.spell);
                elementIcon.SetVerticesDirty();
            }
            displayedElement = element;
        }
        if (!appearanceReady || displayedSelection != selected)
        {
            keyLabel.color = selected ? new Color(.65f, .29f, .045f) : new Color(.18f, .09f, .035f);
            selectionMark.enabled = selected;
            displayedSelection = selected;
        }
        appearanceReady = true;
    }

#if UNITY_EDITOR
    // подключаем копии эмблем обложек после переноса существующей сцены на новые значки.
    public void ConfigureBookSymbols(BookSymbolGraphic icon, Mesh[] symbols)
    {
        bookIcon = icon;
        bookSymbols = symbols;
        appearanceReady = false;
    }

    // сохраняем ссылки при установке, чтобы в игре не искать страницы, значки и коллайдеры.
    public void Configure(int index, BoxCollider collider, GameObject content, Text key,
        Image mark, SpellIconGraphic icon, Spell[] symbols)
    {
        slot = index;
        hitArea = collider;
        pageContent = content;
        keyLabel = key;
        selectionMark = mark;
        elementIcon = icon;
        elementSymbols = symbols;
    }
#endif
}
