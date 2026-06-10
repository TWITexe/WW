using System;

[Serializable]
public class RoomInfo
{
    public string roomName;
    public string address;
    public ushort port;
    public int players;
    public int maxPlayers;

    public RoomInfo(string roomName, string address, ushort port, int players, int maxPlayers)
    {
        this.roomName = roomName;
        this.address = address;
        this.port = port;
        this.players = players;
        this.maxPlayers = maxPlayers;
    }
}