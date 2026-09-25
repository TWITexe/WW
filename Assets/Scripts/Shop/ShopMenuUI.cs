using System;
using System.Linq;
using Mirror;
using TMPro;
using UnityEngine;

public class ShopMenuUI : MonoBehaviour
{
    [Serializable] public class ItemCard
    {
        public GameObject root;
        public UnityEngine.UI.RawImage icon;
        public TMP_Text title, price, actionLabel;
        public UnityEngine.UI.Button action;
    }
    public static ShopMenuUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.modal != null && Instance.modal.activeSelf;
    public Canvas canvas;
    public GameObject modal;
    public UnityEngine.UI.Button opener, close, refresh, reset;
    public UnityEngine.UI.Button[] tabs;
    public TMP_Text wallet, shortcut, status, subtitle;
    public ItemCard[] cards;
    static readonly string[] Categories = { "hat", "staff", "body", "book" };
    int selected;
    EconomyClient client;
    LocalPlayerSettings settings;
    void Awake() { Instance = this; }
    void Start()
    {
        client = EconomyClient.Instance;
        opener.onClick.AddListener(() => Open("hat"));
        close.onClick.AddListener(() => modal.SetActive(false));
        refresh.onClick.AddListener(() => client?.Refresh());
        reset.onClick.AddListener(() => client?.Equip(Categories[selected], ""));
        for (int i = 0; i < tabs.Length; i++) { int index = i; tabs[i].onClick.AddListener(() => { selected = index; Refresh(); }); }
        if (client != null) client.Changed += Refresh;
        settings = LocalPlayerSettings.Instance;
        if (settings != null) settings.PreferredColorChanged += OnColorChanged;
        modal.SetActive(false); Refresh();
    }
    void OnColorChanged(PlayerColorId color) => Refresh();
    void OnDestroy()
    {
        if (client != null) client.Changed -= Refresh;
        if (settings != null) settings.PreferredColorChanged -= OnColorChanged;
        if (Instance == this) Instance = null;
    }
    void Update()
    {
        bool allowed = !NetworkClient.active && !NetworkServer.active;
        canvas.enabled = allowed;
        if (!allowed) modal.SetActive(false);
        if (modal.activeSelf && Input.GetKeyDown(KeyCode.Escape)) modal.SetActive(false);
    }
    public void Open(string category)
    {
        selected = Mathf.Max(0, Array.IndexOf(Categories, category));
        modal.SetActive(true); Refresh(); client?.Refresh();
    }
    public void Refresh()
    {
        var profile = client?.Profile;
        wallet.text = profile == null ? "— W" : $"{profile.coins:N0} W";
        shortcut.text = profile == null ? "МАГАЗИН" : $"МАГАЗИН  ·  {profile.coins:N0} W";
        status.text = client?.Status ?? "Подключение…";
        subtitle.text = selected == 3 ? "Воздух и Лёд открывают новые сочетания стихий. Огонь, Земля и Вода бесплатны." :
            "Ткань и наконечники посохов — в цвет игрока. Ремни шляп и отделка — в цвет стиля.";
        reset.gameObject.SetActive(selected != 3);
        reset.interactable = profile != null && client.Connected && !client.Busy && !string.IsNullOrEmpty(profile.Equipped(Categories[selected]));
        refresh.interactable = client != null && !client.Busy;
        for (int i = 0; i < tabs.Length; i++)
            tabs[i].GetComponent<UnityEngine.UI.Image>().color = i == selected ? new Color(.51f,.32f,.14f) : new Color(.17f,.12f,.09f);
        var items = ShopCatalog.Items.Where(x => x.category == Categories[selected]).ToArray();
        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i]; card.root.SetActive(i < items.Length); if (i >= items.Length) continue;
            var item = items[i]; bool owned = profile != null && profile.Owns(item.id);
            bool equipped = owned && item.category != "book" && profile.Equipped(item.category) == item.id;
            card.title.text = item.name;
            var color = settings != null ? settings.SelectedColor : PlayerColorId.Blue;
            card.icon.texture = (ShopCatalog.IsCosmetic(item) ? Resources.Load<Texture2D>($"Shop/Icons/{color}/{item.id}") : null)
                ?? Resources.Load<Texture2D>("Shop/Icons/" + item.id);
            card.price.text = owned ? (equipped ? "НАДЕТО" : "В КОЛЛЕКЦИИ") : $"{item.price:N0} W";
            var coin = card.price.transform.Find("W coin"); if (coin != null) coin.gameObject.SetActive(!owned);
            card.actionLabel.text = equipped ? "Надето" : owned ? (item.category == "book" ? "Открыто" : "Надеть") : "Купить";
            card.action.interactable = profile != null && client.Connected && !client.Busy && !equipped &&
                (owned ? item.category != "book" : profile.coins >= item.price);
            card.action.onClick.RemoveAllListeners();
            card.action.onClick.AddListener(() => { if (owned) client.Equip(item.category, item.id); else client.Purchase(item.id); });
        }
    }
}
