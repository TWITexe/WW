using Mirror;
using UnityEngine;

// запрашивает цвет у сервера и применяет синхронизированное значение к модели.
public class PlayerColor : NetworkBehaviour
{
    [SerializeField] private Renderer bodyRenderer;

    [SyncVar(hook = nameof(OnColorChanged))]
    private PlayerColorId playerColorId = PlayerColorId.None;

    // отправляем серверу цвет, выбранный владельцем персонажа в меню.
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        PlayerColorId requestedColor = LocalPlayerSettings.Instance.CosmeticSettings.preferredColor;

        CmdRequestColor(requestedColor);
    }

    // освобождаем прежний цвет и резервируем желаемый либо первый свободный.
    [Command]
    private void CmdRequestColor(PlayerColorId requestedColor)
    {
        if (playerColorId != PlayerColorId.None)
            PlayerColorManager.Instance.ReleaseColor(playerColorId);

        playerColorId = PlayerColorManager.Instance.GetColorOrFree(requestedColor);
    }

    // возвращаем цвет в пул после удаления игрока с сервера.
    public override void OnStopServer()
    {
        base.OnStopServer();

        PlayerColorManager.Instance.ReleaseColor(playerColorId);
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
        if (wizard != null && PlayerColorManager.Instance != null)
        {
            wizard.Tint(PlayerColorManager.Instance.GetUnityColor(colorId));
            return;
        }
        if (bodyRenderer == null)
            return;

        Color unityColor = PlayerColorManager.Instance.GetUnityColor(colorId);
        bodyRenderer.material.color = unityColor;
    }
}
