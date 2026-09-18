using System.Collections.Generic;
using Mirror;
using UnityEngine;

// обновляет список комнат вручную и при каждом открытии меню портала.
public class RoomBrowserController : MonoBehaviour
{
    [SerializeField] private MonoBehaviour providerBehaviour;
    [SerializeField] private RoomListUI roomListUI;
    private IRoomProvider roomProvider;

    // получаем поставщика комнат по сохранённой ссылке.
    private void Awake()
    {
        roomProvider = providerBehaviour as IRoomProvider;
        if (roomProvider == null)
        {
            Debug.LogError("Room provider must implement IRoomProvider.");
            return;
        }
        roomProvider.RoomsUpdated += OnRoomsUpdated;
    }

    // подписка не зависит от расположения контроллера вне скрываемого окна.
    private void OnEnable() => PortalConnectMenu.Opened += Refresh;
    private void OnDisable() => PortalConnectMenu.Opened -= Refresh;

    // освобождаем подписку при смене сцены.
    private void OnDestroy()
    {
        if (roomProvider != null) roomProvider.RoomsUpdated -= OnRoomsUpdated;
    }

    // очищаем устаревшие строки сразу, даже если на новый поиск не ответит ни одна комната.
    public void Refresh()
    {
        if (roomProvider == null || NetworkClient.active || NetworkServer.active) return;
        roomListUI.ShowRooms(new List<RoomInfo>());
        roomProvider.RefreshRooms();
    }

    // показываем ответы по мере обнаружения серверов.
    private void OnRoomsUpdated(List<RoomInfo> rooms) => roomListUI.ShowRooms(rooms);
}
