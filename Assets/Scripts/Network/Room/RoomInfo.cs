using System;

// содержит имя комнаты, адрес подключения и количество занятых и доступных мест.
[Serializable]
public class RoomInfo
{
    public string roomName;
    public string address;
    public ushort port;
    public int players;
    public int maxPlayers;
    public bool isPrivate;
    public MatchRules rules;

    // заполняем данные одной комнаты из ответа выбранного источника.
    public RoomInfo(string roomName, string address, ushort port, int players, int maxPlayers)
    {
        this.roomName = roomName;
        this.address = address;
        this.port = port;
        this.players = players;
        this.maxPlayers = maxPlayers;
    }
}
