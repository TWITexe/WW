using System;
using Mirror;
using UnityEngine;

public enum RoomGameMode : byte { Deathmatch }
public enum MatchPhase : byte { Waiting, Playing, Results }

[Serializable]
public struct MatchRules
{
    public RoomGameMode mode;
    public int minutes;
    public int killGoal;
    public static MatchRules Default => new MatchRules { minutes = 10, killGoal = 15 };
    public MatchRules Validated() => new MatchRules
    {
        mode = RoomGameMode.Deathmatch,
        minutes = Mathf.Clamp(minutes, 1, 30),
        killGoal = Mathf.Clamp(killGoal, 1, 33)
    };
    public static string Clock(double seconds)
    {
        int value = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
        return $"{value / 60:00}:{value % 60:00}";
    }
    public static Color TimerColor(double remaining, double total) =>
        Color.Lerp(Color.red, Color.white, Mathf.Clamp01((float)(remaining / (total * .3))));
}

[Serializable]
public struct MatchStanding
{
    public string playerId;
    public string name;
    public Color color;
    public int kills;
    public int deaths;
}

public struct MatchStateMessage : NetworkMessage
{
    public MatchRules rules;
    public MatchPhase phase;
    public int round;
    public double endsAt;
    public double rematchAt;
    public string reason;
    public MatchStanding[] standings;
    public int votes;
    public int players;
    public bool voted;
}
public struct RematchVoteMessage : NetworkMessage { public int round; }
public struct RoomExitMessage : NetworkMessage { public string reason; }
