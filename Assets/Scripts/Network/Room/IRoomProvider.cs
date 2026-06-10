using System;
using System.Collections.Generic;

public interface IRoomProvider
{
    event Action<List<RoomInfo>> RoomsUpdated;

    void RefreshRooms();
}