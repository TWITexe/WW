using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ColorShopUI : MonoBehaviour
{
    public TMP_Text wallet, status;
    public Button refresh;
    EconomyClient client;
    void OnEnable() => Bind();
    void Start() { refresh.onClick.AddListener(() => client?.Refresh()); Bind(); }
    void Bind()
    {
        if (client == null && EconomyClient.Instance != null)
        {
            client = EconomyClient.Instance;
            client.Changed += Refresh;
        }
        Refresh();
    }
    void Refresh()
    {
        if (wallet != null) wallet.text = client?.Profile == null ? "— W" : $"{client.Profile.coins:N0} W";
        if (status != null) status.text = client != null ? client.Status : "Нажмите на цвет, чтобы купить или выбрать. Покупка навсегда.";
        if (refresh != null) refresh.interactable = client != null && !client.Busy;
    }
    void OnDisable() { if (client != null) client.Changed -= Refresh; client = null; }
}
