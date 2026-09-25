using Mirror;
using UnityEngine;

// запрашивает цвет у сервера и применяет синхронизированное значение к модели.
public class PlayerColor : NetworkBehaviour
{
    [SerializeField] private Renderer bodyRenderer;

    [SyncVar(hook = nameof(OnColorChanged))]
    private PlayerColorId playerColorId = PlayerColorId.Blue;
    public Color DisplayColor => PlayerColorManager.ToUnityColor(playerColorId);

    // отправляем серверу цвет, выбранный владельцем персонажа в меню.
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        PlayerColorId requestedColor = LocalPlayerSettings.Instance != null ? LocalPlayerSettings.Instance.SelectedColor : PlayerColorId.Blue;

        CmdRequestColor(requestedColor);
    }

    // Проверяем выбор по инвентарю, переданному хосту при входе в комнату.
    [Command]
    private void CmdRequestColor(PlayerColorId requestedColor)
    {
        var profile = NetManager.Room?.RoomAuth.ProfileFor(connectionToClient);
        playerColorId = ShopCatalog.Allows(profile, requestedColor) ? requestedColor : PlayerColorId.Blue;
    }

    // применяем новый цвет после сетевого изменения SyncVar.
    private void OnColorChanged(PlayerColorId oldColor, PlayerColorId newColor)
    {
        ApplyColor(newColor);
    }

    // окрашиваем уже существующего персонажа при его первом появлении на клиенте.
    public override void OnStartClient()
    {
        base.OnStartClient();

        ApplyColor(playerColorId);
    }

    // используем окраску частей мага, а при её отсутствии — запасной Renderer.
    private void ApplyColor(PlayerColorId colorId)
    {
        var wizard = GetComponent<WizardAppearance>();
        if (wizard != null)
        {
            wizard.Tint(PlayerColorManager.ToUnityColor(colorId));
            return;
        }
        if (bodyRenderer == null)
            return;

        Color unityColor = PlayerColorManager.ToUnityColor(colorId);
        bodyRenderer.material.color = unityColor;
    }
}
