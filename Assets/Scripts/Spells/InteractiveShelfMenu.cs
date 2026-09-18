using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;

// управляет переходом от общего ракурса к полкам и назначением стихий через настоящие книги.
public class InteractiveShelfMenu : MonoBehaviour
{
    [SerializeField] private Camera menuCamera;
    [SerializeField] private BoxCollider shelfHitArea;
    [SerializeField] private ElementBook[] books;
    [SerializeField] private ShelfSpellCatalogUI catalog;
    [SerializeField] private OpenBookSlot[] openBookSlots = new OpenBookSlot[0];
    [SerializeField] private DeskCustomizationUI customizationDesk;
    [SerializeField] private PortalConnectMenu portalMenu;
    [SerializeField] private Vector3 portalPosition = new Vector3(-3.16f, .97f, -11.88f);
    [SerializeField] private Vector3 portalAngles = new Vector3(8.23f, 180, 0);
    [SerializeField] private Vector3 deskPosition = new Vector3(-4.65f, 1.32f, -7.8f);
    [SerializeField] private Vector3 deskAngles = new Vector3(26.82f, -90.7f, 0);
    [SerializeField] private GameObject[] overviewOnlyObjects;
    [SerializeField] private Vector3 overviewPosition = new Vector3(-5.54f, 1.66f, -6.94f);
    [SerializeField] private Vector3 overviewAngles = new Vector3(20.5f, 137.4f, 0);
    [SerializeField] private Vector3 shelfPosition = new Vector3(-3.8f, .8f, -8.2f);
    [SerializeField] private Vector3 shelfAngles = new Vector3(9.16f, 94.78f, 0);
    [SerializeField, Min(.1f)] private float flightDuration = .9f;

    // переходы отделены от устойчивых ракурсов, чтобы нажатие во время полёта не назначило стихию случайно.
    private enum ViewState { Overview, FlyingToShelf, Shelf, FlyingBack, FlyingToDesk, Desk, FlyingToPortal, Portal }
    private ViewState state;
    private Vector3 flightStartPosition;
    private Quaternion flightStartRotation;
    private float flightTime;
    private int selectedSlot;
    private ElementBook hoveredBook;
    private bool[] originalVisibility;

    public bool IsFocused => state == ViewState.Shelf;
    public Camera MenuCamera => menuCamera;
    public ElementBook[] Books => books;
    public BoxCollider ShelfHitArea => shelfHitArea;
    public ShelfSpellCatalogUI Catalog => catalog;
    public OpenBookSlot[] OpenBookSlots => openBookSlots;
    public DeskCustomizationUI CustomizationDesk => customizationDesk;
    public PortalConnectMenu PortalMenu => portalMenu;

    // устанавливаем общий ракурс и скрываем каталог до завершения подлёта.
    private void Start()
    {
        originalVisibility = new bool[overviewOnlyObjects.Length];
        for (int index = 0; index < overviewOnlyObjects.Length; index++)
            originalVisibility[index] = overviewOnlyObjects[index] != null && overviewOnlyObjects[index].activeSelf;
        menuCamera.transform.SetPositionAndRotation(overviewPosition, Quaternion.Euler(overviewAngles));
        catalog.Bind(this);
        //catalog.gameObject.SetActive(false);
        //SetSlotBooksVisible(false);
        RefreshSelection();
    }

    // принимаем ввод только вне сетевого матча и после завершения перехода камеры.
    private void Update()
    {
        if (menuCamera == null || catalog == null) return;
        if (NetworkClient.active || NetworkServer.active)
        {
            catalog.gameObject.SetActive(false);
            SetSlotBooksVisible(false);
            if (customizationDesk != null) customizationDesk.SetInteraction(false);
            if (portalMenu != null) portalMenu.SetInteraction(false);
            return;
        }

        if (state != ViewState.Overview && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)))
            BeginFlight(false);

        if (state == ViewState.FlyingToShelf || state == ViewState.FlyingBack || state == ViewState.FlyingToDesk || state == ViewState.FlyingToPortal)
        {
            UpdateFlight();
            return;
        }

        Ray ray = menuCamera.ScreenPointToRay(Input.mousePosition);
        bool overInterface = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (state == ViewState.Overview)
        {
            if (!overInterface && Input.GetMouseButtonDown(0))
            {
                if (portalMenu != null && portalMenu.HitArea.Raycast(ray, out _, 30f))
                    BeginPortalFlight();
                else if (customizationDesk != null && customizationDesk.HatHitArea.Raycast(ray, out _, 30f))
                    BeginDeskFlight();
                else if (shelfHitArea.Raycast(ray, out _, 30f)) BeginFlight(true);
            }
            return;
        }
        // ввод имени на планшете не должен одновременно выбирать слоты стихий на полках.
        if (state == ViewState.Desk) return;
        if (state == ViewState.Portal)
        {
            portalMenu.UpdateAppearance(Time.unscaledDeltaTime);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Q)) SelectSlot(0);
        if (Input.GetKeyDown(KeyCode.E)) SelectSlot(1);
        if (Input.GetKeyDown(KeyCode.R)) SelectSlot(2);

        // открытая книга выбирает слот; книги стихий ниже назначают выбранному слоту элемент.
        if (!overInterface && Input.GetMouseButtonDown(0))
        {
            OpenBookSlot slotBook = GetSlotBook(ray);
            if (slotBook != null)
            {
                SelectSlot(slotBook.Slot);
                return;
            }
        }

        ElementBook target = overInterface ? null : FindBook(ray);
        if (target != hoveredBook)
        {
            hoveredBook = target;
            RefreshBookColors();
        }
        if (target != null && Input.GetMouseButtonDown(0)) AssignBook(target);
    }

    // выбираем ближайшую книгу из собственных областей нажатия, не зависим от коллайдеров декораций.
    public ElementBook FindBook(Ray ray)
    {
        ElementBook nearest = null;
        float distance = float.MaxValue;
        foreach (ElementBook book in books)
            if (book != null && book.Raycast(ray, out RaycastHit hit) && hit.distance < distance)
            {
                nearest = book;
                distance = hit.distance;
            }
        return nearest;
    }

    // выбираем ближайший разворот из трёх сохранённых ссылок без глобального поиска.
    public OpenBookSlot GetSlotBook(Ray ray)
    {
        OpenBookSlot nearest = null;
        float distance = float.MaxValue;
        foreach (OpenBookSlot book in openBookSlots)
            if (book != null && book.Raycast(ray, out RaycastHit hit) && hit.distance < distance)
            {
                nearest = book;
                distance = hit.distance;
            }
        return nearest;
    }

    // управляем только надписями на страницах, не выключая декоративные модели книг.
    private void SetSlotBooksVisible(bool visible)
    {
        foreach (OpenBookSlot book in openBookSlots)
            if (book != null) book.SetVisible(visible);
    }

    // запоминаем текущий ракурс, чтобы обратный переход был плавным даже посреди подлёта.
    public void BeginFlight(bool toShelf)
    {
        if (NetworkClient.active || NetworkServer.active) return;
        if (toShelf && state != ViewState.Overview) return;
        StartFlight(toShelf ? ViewState.FlyingToShelf : ViewState.FlyingBack);
    }

    // открываем настройку персонажа только из общего ракурса, как и выбор стихий.
    public void BeginDeskFlight()
    {
        if (state != ViewState.Overview || customizationDesk == null || NetworkClient.active || NetworkServer.active) return;
        StartFlight(ViewState.FlyingToDesk);
    }

    // портал открывает подключение только из общего ракурса и вне запущенной сетевой сессии.
    public void BeginPortalFlight()
    {
        if (state != ViewState.Overview || portalMenu == null || NetworkClient.active || NetworkServer.active) return;
        StartFlight(ViewState.FlyingToPortal);
    }

    // кнопка на планшете возвращает камеру, но не скрывает сам интерфейс.
    public void ReturnToOverview() => BeginFlight(false);

    // все направления используют один переход, поэтому два скрипта не двигают камеру одновременно.
    private void StartFlight(ViewState flightState)
    {
        flightStartPosition = menuCamera.transform.position;
        flightStartRotation = menuCamera.transform.rotation;
        flightTime = 0;
        state = flightState;
        if (portalMenu != null) portalMenu.Hide();
        if (customizationDesk != null) customizationDesk.SetInteraction(false);
        hoveredBook = null;
        //catalog.gameObject.SetActive(false);
        //SetSlotBooksVisible(false);
        SetOverviewObjects(false);
        RefreshBookColors();
    }

    // сглаживаем начало и конец движения, используя время, независимое от игрового масштаба времени.
    private void UpdateFlight()
    {
        flightTime += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(flightTime / flightDuration);
        float smoothProgress = progress * progress * (3f - 2f * progress);
        bool toShelf = state == ViewState.FlyingToShelf;
        bool toDesk = state == ViewState.FlyingToDesk;
        bool toPortal = state == ViewState.FlyingToPortal;
        
        Vector3 destination = toPortal ? portalPosition : toDesk ? deskPosition : toShelf ? shelfPosition : overviewPosition;
        
        Quaternion rotation = Quaternion.Euler(toPortal ? portalAngles : toDesk ? deskAngles : toShelf ? shelfAngles : overviewAngles);
        menuCamera.transform.SetPositionAndRotation(
            Vector3.Lerp(flightStartPosition, destination, smoothProgress),
            Quaternion.Slerp(flightStartRotation, rotation, smoothProgress));

        if (progress < 1f) return;
        state = toPortal ? ViewState.Portal : toDesk ? ViewState.Desk : toShelf ? ViewState.Shelf : ViewState.Overview;
        if (customizationDesk != null) customizationDesk.SetInteraction(toDesk);
        //catalog.gameObject.SetActive(toShelf);
        //SetSlotBooksVisible(toShelf);
        SetOverviewObjects(state == ViewState.Overview);
        if (toPortal) portalMenu.Show();
        RefreshSelection();
    }

    // меняем слот назначения; клавиши стихий в меню не запускают боевые комбинации.
    public void SelectSlot(int slot)
    {
        if (!IsFocused || slot < 0 || slot > 2) return;
        selectedSlot = slot;
        RefreshSelection();
    }

    // используем существующие правила перестановки и сохранения стихий, не изменяя сетевую систему.
    public void AssignBook(ElementBook book)
    {
        if (!IsFocused || book == null || LocalPlayerSettings.Instance == null) return;
        LocalPlayerSettings.Instance.SetElement(selectedSlot, book.Element);
        RefreshSelection();
    }

    // обновляем каталог после назначения и согласовываем яркость всех пяти книг с набором игрока.
    private void RefreshSelection()
    {
        ElementLoadout loadout = LocalPlayerSettings.Instance != null ? LocalPlayerSettings.Instance.Loadout : ElementLoadout.Default;
        if (catalog.gameObject.activeInHierarchy) catalog.Refresh(loadout, selectedSlot);
        foreach (OpenBookSlot book in openBookSlots)
            if (book != null) book.Refresh(loadout, selectedSlot);
        RefreshBookColors();
    }

    // активными считаются все три выбранные стихии, а не только книга текущего слота.
    private void RefreshBookColors()
    {
        ElementLoadout loadout = LocalPlayerSettings.Instance != null ? LocalPlayerSettings.Instance.Loadout : ElementLoadout.Default;
        foreach (ElementBook book in books)
            if (book != null) book.SetAppearance(loadout.Contains(book.Element), book == hoveredBook);
    }

    // прячем оставшиеся элементы главного меню на время работы с полками, сохраняя их исходную видимость.
    private void SetOverviewObjects(bool visible)
    {
        if (originalVisibility == null) return;
        for (int index = 0; index < overviewOnlyObjects.Length; index++)
            if (overviewOnlyObjects[index] != null)
                overviewOnlyObjects[index].SetActive(visible && originalVisibility[index]);
    }

#if UNITY_EDITOR
    // холст остаётся включённым для connectui; скрываются только остальные дочерние окна главного меню.
    public void ConfigurePortal(PortalConnectMenu portal, GameObject[] overviewObjects)
    {
        portalMenu = portal;
        overviewOnlyObjects = overviewObjects;
    }

    // подключаем постоянно видимый интерфейс на письменном столе.
    public void ConfigureDesk(DeskCustomizationUI desk) => customizationDesk = desk;

    // подключаем открытые книги после их расстановки в редакторе.
    public void ConfigureSlotBooks(OpenBookSlot[] slots) => openBookSlots = slots;

    // сохраняем ссылки в сцене; расположение камеры и длительность полёта доступны в инспекторе.
    public void Configure(Camera camera, BoxCollider shelf, ElementBook[] elementBooks,
        ShelfSpellCatalogUI worldCatalog, GameObject[] overviewObjects)
    {
        menuCamera = camera;
        shelfHitArea = shelf;
        books = elementBooks;
        catalog = worldCatalog;
        overviewOnlyObjects = overviewObjects;
    }
#endif
}
