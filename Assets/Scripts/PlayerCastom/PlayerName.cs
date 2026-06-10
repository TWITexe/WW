using Mirror;
using TMPro;
using UnityEngine;

public class PlayerName : NetworkBehaviour
{
    [SerializeField] private TMP_Text nicknameText;

    [SyncVar(hook = nameof(OnNicknameChanged))]
    private string nickname;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        string requestedNickname = LocalPlayerSettings.Instance.CosmeticSettings.nickname;

        CmdRequestNickname(requestedNickname);
    }

    [Command]
    private void CmdRequestNickname(string requestedNickname)
    {
        nickname = ValidateNickname(requestedNickname);
    }

    private string ValidateNickname(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Player";

        value = value.Trim();

        if (value.Length > 16)
            value = value.Substring(0, 16);

        return value;
    }

    private void OnNicknameChanged(string oldName, string newName)
    {
        ApplyNickname(newName);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        ApplyNickname(nickname);
    }

    private void ApplyNickname(string value)
    {
        if (nicknameText != null)
            nicknameText.text = value;
    }
}