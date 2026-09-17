using UnityEngine;

public class LocalPlayerSettings : MonoBehaviour
{
    public static LocalPlayerSettings Instance { get; private set; }

    public PlayerCosmeticSettings CosmeticSettings { get; private set; }
    public ElementLoadout Loadout { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Loadout = new ElementLoadout
        {
            q = (MagicElement)PlayerPrefs.GetInt("Elements.Q", 0),
            e = (MagicElement)PlayerPrefs.GetInt("Elements.E", 1),
            r = (MagicElement)PlayerPrefs.GetInt("Elements.R", 2)
        };
        if (!Loadout.IsValid) Loadout = ElementLoadout.Default;

        CosmeticSettings = new PlayerCosmeticSettings
        {
            preferredColor = PlayerColorId.Blue,
            nickname = "Player"
        };

        DontDestroyOnLoad(gameObject);
    }

    public void SetPreferredColor(PlayerColorId colorId)
    {
        CosmeticSettings.preferredColor = colorId;
    }

    public void SetNickname(string nickname)
    {
        CosmeticSettings.nickname = nickname;
    }

    public void SetElement(int slot, MagicElement element)
    {
        if (Mirror.NetworkClient.active || Mirror.NetworkServer.active) return;
        ElementLoadout updated = Loadout;
        updated.Assign(slot, element);
        if (!updated.IsValid) return;
        Loadout = updated;
        PlayerPrefs.SetInt("Elements.Q", (int)updated.q);
        PlayerPrefs.SetInt("Elements.E", (int)updated.e);
        PlayerPrefs.SetInt("Elements.R", (int)updated.r);
        PlayerPrefs.Save();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
