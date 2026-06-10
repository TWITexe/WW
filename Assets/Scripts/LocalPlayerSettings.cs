using UnityEngine;

public class LocalPlayerSettings : MonoBehaviour
{
    public static LocalPlayerSettings Instance { get; private set; }

    public PlayerCosmeticSettings CosmeticSettings { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

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
}