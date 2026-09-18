using Mirror;
using TMPro;
using UnityEngine;

// отправляет имя серверу и обновляет подпись после сетевой синхронизации.
public class PlayerName : NetworkBehaviour
{
    [SerializeField] private TMP_Text nicknameText;

    [SyncVar(hook = nameof(OnNicknameChanged))]
    private string nickname;
    public string Nickname => string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname;

    // запрашиваем имя из локальных настроек только для собственного персонажа.
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        string requestedNickname = LocalPlayerSettings.Instance.CosmeticSettings.nickname;

        CmdRequestNickname(requestedNickname);
    }

    // сервер проверяет имя перед записью в синхронизируемое поле.
    [Command]
    private void CmdRequestNickname(string requestedNickname)
    {
        nickname = ValidateNickname(requestedNickname);
    }

    // убираем пробелы по краям, ограничиваем имя шестнадцатью символами и заменяем пустое стандартным.
    private string ValidateNickname(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Player";

        value = value.Trim();

        if (value.Length > 16)
            value = value.Substring(0, 16);

        return value;
    }

    // обновляем подпись после изменения имени по сети.
    private void OnNicknameChanged(string oldName, string newName)
    {
        ApplyNickname(newName);
    }

    // показываем начальное имя, даже если после появления персонажа оно больше не меняется.
    public override void OnStartClient()
    {
        base.OnStartClient();

        ApplyNickname(nickname);
    }

    // записываем имя в назначенный текстовый элемент.
    private void ApplyNickname(string value)
    {
        if (nicknameText != null)
            nicknameText.text = value;
    }
}
