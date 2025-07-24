using System.Collections.Generic;
using UnityEngine;

public class SpellManager : MonoBehaviour
{

    [SerializeField] List<Spell> spells;
    private PlayerNetworkCaster caster;
    private InputComboTracker inputComboTracker;

    private Dictionary<Spell, float> spellCooldowns = new Dictionary<Spell, float>(); // кулдауны спеллов
    List<KeyCode> keysForSpells = new List<KeyCode>() { KeyCode.Q, KeyCode.E,KeyCode.R }; // кнопки для скиллов

    private void Start()
    {
        // тут в общем если слева нул, то юзается правая часть
        inputComboTracker = GetComponent<InputComboTracker>() ?? gameObject.AddComponent<InputComboTracker>();
        caster = GetComponent<PlayerNetworkCaster>();

    }

    void Update()
    {
       

        foreach (var key in keysForSpells)
        {
            if (Input.GetKeyDown(key))
            {
                Debug.Log($"Key {key} down!");
                inputComboTracker.AddKey(key);

                foreach (var spell in spells)
                {
                    if (spell.MatchesCombo(inputComboTracker.GetHistory()) && CanCast(spell))
                    {
                        spell.Activate(caster);
                    }
                }
            }
        }
    }
    public void AddSpell(Spell spell)
    {
        if (!spells.Contains(spell))
            spells.Add(spell);
    }

    public void RemoveSpell(Spell spell)
    {
        if (spells.Contains(spell))
            spells.Remove(spell);
    }
    bool CanCast(Spell spell)
    {
        if (!spellCooldowns.ContainsKey(spell)) return true;
        return Time.time - spellCooldowns[spell] >= spell.Cooldown;
    }

    void SetCastTime(Spell spell)
    {
        spellCooldowns[spell] = Time.time;
    }
}
