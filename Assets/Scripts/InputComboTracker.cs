using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class InputComboTracker : MonoBehaviour
{
    [SerializeField] List<KeyCode> historyInput = new List<KeyCode>();
    private int maxLength = 3;
    public void AddKey(KeyCode key)
    {
        historyInput.Add(key);
        if (historyInput.Count > maxLength)
        {
            historyInput.RemoveAt(0);
        }
    }
    public List<KeyCode> GetHistory()
    {
        return historyInput;
    }
}
