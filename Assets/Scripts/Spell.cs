
using System.Collections.Generic;
using UnityEngine;

public abstract class Spell : ScriptableObject
{
    public abstract KeyCode[] Combo { get; }
    protected string spellName;
    [SerializeField] protected float cooldown = 1f;
    public float Cooldown => cooldown;
    protected float lastCastTime = -9999f;
    public float LastCastTime => lastCastTime;
    public string Name => spellName;

    public bool MatchesCombo(List<KeyCode> inputHistory) // проверка комбо
    {
        if (inputHistory.Count < Combo.Length)
            return false;

        for (int i = 0; i < Combo.Length; i++)
        {
            if (inputHistory[inputHistory.Count - Combo.Length + i] != Combo[i])
                return false;
        }
        return true;
    }

    public abstract void Activate(PlayerNetworkCaster caster);
}
