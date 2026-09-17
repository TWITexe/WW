using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(200)]
public class SpellManager : MonoBehaviour
{
    [SerializeField] private List<Spell> spells = new List<Spell>();
    private static readonly KeyCode[] Keys = { KeyCode.Q, KeyCode.E, KeyCode.R };
    private PlayerNetworkCaster caster;
    private InputComboTracker tracker;
    private Health health;
    public IReadOnlyList<Spell> Spells => spells;
    private void Awake()
    {
        tracker = GetComponent<InputComboTracker>() ?? gameObject.AddComponent<InputComboTracker>();
        caster = GetComponent<PlayerNetworkCaster>();
        health = GetComponent<Health>();
    }
    private void LateUpdate()
    {
        if (caster == null || !caster.isLocalPlayer) return;
        tracker.Expire();
        if (PlayerGameUI.InputBlocked || !caster.LoadoutReady || (health != null && health.IsDead))
        {
            tracker.Clear();
            return;
        }
        for (int slot = 0; slot < Keys.Length; slot++)
        {
            if (!Input.GetKeyDown(Keys[slot])) continue;
            tracker.AddElement(caster.Loadout.Get(slot));
            caster.SubmitElement(slot);
            if (FindSpell(tracker.History, caster.Loadout) >= 0) tracker.Clear();
        }
    }
    public int FindSpell(IReadOnlyList<MagicElement> input, ElementLoadout loadout)
    {
        for (int i = 0; i < spells.Count; i++)
            if (spells[i] != null && spells[i].IsAvailable(loadout) && spells[i].MatchesCombo(input)) return i;
        return -1;
    }
    public Spell GetSpell(int index) => index >= 0 && index < spells.Count ? spells[index] : null;
}
