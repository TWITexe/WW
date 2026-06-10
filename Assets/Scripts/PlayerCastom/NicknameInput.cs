using TMPro;
using UnityEngine;

public class NicknameInput : MonoBehaviour
{
    [SerializeField] private TMP_InputField nicknameInput;

    private void Start()
    {
        nicknameInput.text = LocalPlayerSettings.Instance.CosmeticSettings.nickname;

        nicknameInput.onValueChanged.AddListener(OnNicknameChanged);
    }

    private void OnNicknameChanged(string value)
    {
        LocalPlayerSettings.Instance.SetNickname(value);
    }
}