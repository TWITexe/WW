using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomListItem : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text playersText;
    //[SerializeField] private TMP_Text addressText;
    [SerializeField] private Button joinButton;

    private RoomInfo roomInfo;
    private RoomListUI roomListUI;

    public void Setup(RoomInfo info, RoomListUI owner)
    {
        roomInfo = info;
        roomListUI = owner;

        roomNameText.text = info.roomName;
        playersText.text = $"{info.players}/{info.maxPlayers}";
        //addressText.text = $"{info.address}:{info.port}";

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(Join);
    }

    private void Join()
    {
        roomListUI.JoinRoom(roomInfo);
    }
}