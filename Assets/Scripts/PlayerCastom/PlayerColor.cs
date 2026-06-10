using Mirror;
using UnityEngine;

public class PlayerColor : NetworkBehaviour
{
    [SerializeField] private Renderer bodyRenderer;

    [SyncVar(hook = nameof(OnColorChanged))]
    private PlayerColorId playerColorId = PlayerColorId.None;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        PlayerColorId requestedColor = LocalPlayerSettings.Instance.CosmeticSettings.preferredColor;

        CmdRequestColor(requestedColor);
    }

    [Command]
    private void CmdRequestColor(PlayerColorId requestedColor)
    {
        if (playerColorId != PlayerColorId.None)
            PlayerColorManager.Instance.ReleaseColor(playerColorId);

        playerColorId = PlayerColorManager.Instance.GetColorOrFree(requestedColor);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        PlayerColorManager.Instance.ReleaseColor(playerColorId);
    }

    private void OnColorChanged(PlayerColorId oldColor, PlayerColorId newColor)
    {
        ApplyColor(newColor);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        ApplyColor(playerColorId);
    }

    private void ApplyColor(PlayerColorId colorId)
    {
        if (bodyRenderer == null)
            return;

        Color unityColor = PlayerColorManager.Instance.GetUnityColor(colorId);
        bodyRenderer.material.color = unityColor;
    }
}