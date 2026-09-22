using TMPro;
using UnityEngine;

// переносит введённое в меню имя в настройки локального игрока.
public class NicknameInput : MonoBehaviour
{
    [SerializeField] private TMP_InputField nicknameInput;

    // показываем текущее имя и подписываемся на последующие изменения поля.
    private void Start()
    {
        nicknameInput.text = LocalPlayerSettings.Instance.CosmeticSettings.nickname;

        nicknameInput.onValueChanged.AddListener(OnNicknameChanged);
        nicknameInput.onEndEdit.AddListener(OnEditingFinished);
    }

    // завершаем запись при выходе из поля и снимаем подписки вместе с меню.
    private void OnEditingFinished(string value) => LocalPlayerSettings.Instance?.SaveNickname();
    private void OnDestroy()
    {
        if (nicknameInput == null) return;
        nicknameInput.onValueChanged.RemoveListener(OnNicknameChanged);
        nicknameInput.onEndEdit.RemoveListener(OnEditingFinished);
        LocalPlayerSettings.Instance?.SaveNickname();
    }

    // сохраняем текст поля для последующей отправки серверу.
    private void OnNicknameChanged(string value)
    {
        LocalPlayerSettings.Instance.SetNickname(value);
    }
}