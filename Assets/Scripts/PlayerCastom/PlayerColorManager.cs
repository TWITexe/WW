using System.Collections.Generic;
using UnityEngine;

public class PlayerColorManager : MonoBehaviour
{
    public static PlayerColorManager Instance { get; private set; }

    private readonly List<PlayerColorId> availableColors = new()
    {
        PlayerColorId.Red,
        PlayerColorId.Blue,
        PlayerColorId.Green,
        PlayerColorId.Yellow,
        PlayerColorId.Cyan,
        PlayerColorId.Magenta,
        PlayerColorId.Purple,
        PlayerColorId.Lime,
        PlayerColorId.Orange,
        PlayerColorId.White
    };

    private readonly HashSet<PlayerColorId> usedColors = new();

    private void Awake()
    {
        Instance = this;
    }

    public PlayerColorId GetColorOrFree(PlayerColorId requestedColor)
    {
        if (TryReserveColor(requestedColor))
            return requestedColor;

        return GetFreeColor();
    }

    public PlayerColorId GetFreeColor()
    {
        foreach (PlayerColorId colorId in availableColors)
        {
            if (!usedColors.Contains(colorId))
            {
                usedColors.Add(colorId);
                return colorId;
            }
        }

        Debug.LogWarning("Свободные цвета закончились!");
        return PlayerColorId.None;
    }

    public bool TryReserveColor(PlayerColorId colorId)
    {
        if (colorId == PlayerColorId.None)
            return false;

        if (!availableColors.Contains(colorId))
            return false;

        if (usedColors.Contains(colorId))
            return false;

        usedColors.Add(colorId);
        return true;
    }

    public void ReleaseColor(PlayerColorId colorId)
    {
        if (colorId == PlayerColorId.None)
            return;

        usedColors.Remove(colorId);
    }

    public Color GetUnityColor(PlayerColorId colorId)
    {
        return colorId switch
        {
            PlayerColorId.Red => Color.red,
            PlayerColorId.Blue => Color.blue,
            PlayerColorId.Green => Color.green,
            PlayerColorId.Yellow => Color.yellow,
            PlayerColorId.Cyan => Color.cyan,
            PlayerColorId.Magenta => Color.magenta,
            PlayerColorId.Purple => new Color(0.5f, 0f, 1f),
            PlayerColorId.Lime => new Color(0.3f, 1f, 0.3f),
            PlayerColorId.Orange => new Color(1f, 0.5f, 0f),
            PlayerColorId.White => Color.white,
            _ => Color.gray
        };
    }
}