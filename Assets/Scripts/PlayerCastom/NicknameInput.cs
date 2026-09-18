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
    }

    // сохраняем текст поля для последующей отправки серверу.
    private void OnNicknameChanged(string value)
    {
        LocalPlayerSettings.Instance.SetNickname(value);
    }
}