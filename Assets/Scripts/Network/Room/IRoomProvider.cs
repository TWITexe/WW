using System;
using System.Collections.Generic;

// общий договор для источника комнат: запрос обновления и событие с найденным списком.
public interface IRoomProvider
{
    event Action<List<RoomInfo>> RoomsUpdated;

    // запрашиваем обновление; результат источник должен передать через событие RoomsUpdated.
    void RefreshRooms();
}