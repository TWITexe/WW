using TMPro;
using UnityEngine;
using UnityEngine.UI;

// отображает одну комнату и подключает игрока к её адресу по кнопке.
public class RoomListItem : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private Button joinButton;

    private RoomInfo roomInfo;
    private RoomListUI roomListUI;

    // заполняем подписи комнаты и назначаем обработчик подключения.
    public void Setup(RoomInfo info, RoomListUI owner)
    {
        roomInfo = info;
        roomListUI = owner;

        roomNameText.richText = false;
        roomNameText.text = string.IsNullOrWhiteSpace(info.roomName) ? "Комната" : info.roomName;
        if (info.isPrivate) roomNameText.text += " · Закрытая";
        playersText.text = $"{info.players}/{info.maxPlayers}";
        // адрес хранится в данных комнаты и используется при подключении, но не выводится отдельной подписью.

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(Join);
        joinButton.interactable = info.players < info.maxPlayers;
    }

    // передаём выбранную комнату владельцу списка, который настроит и запустит подключение.
    private void Join()
    {
        roomListUI.JoinRoom(roomInfo);
    }
}
