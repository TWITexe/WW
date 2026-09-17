using System.Collections.Generic;
using UnityEngine;

public class InputComboTracker : MonoBehaviour
{
    public const float InputTimeout = 2f;
    private readonly List<MagicElement> history = new List<MagicElement>(3);
    private float lastInput;
    public IReadOnlyList<MagicElement> History => history;
    public void AddElement(MagicElement element)
    {
        Expire();
        if (history.Count == 3) history.RemoveAt(0);
        history.Add(element);
        lastInput = Time.time;
    }
    public void Expire()
    {
        if (Time.time - lastInput >= InputTimeout) Clear();
    }
    public void Clear() => history.Clear();
}
