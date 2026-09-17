using System.Collections.Generic;
using UnityEngine;

public abstract class Spell : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    public string Description => description;
    [SerializeField] private MagicElement[] recipe;
    [SerializeField, Min(0.1f)] protected float cooldown = 1f;
    public float Cooldown => Mathf.Max(0.1f, cooldown);
    public string Name => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public IReadOnlyList<MagicElement> Recipe => recipe;
    public bool IsAvailable(ElementLoadout loadout)
    {
        if (!loadout.IsValid || recipe == null || recipe.Length != 3) return false;
        foreach (MagicElement element in recipe)
            if (!loadout.Contains(element)) return false;
        return true;
    }
    // Order-independent recipes, preserving the number of each element.
    public bool MatchesCombo(IReadOnlyList<MagicElement> input)
    {
        if (recipe == null || recipe.Length != 3 || input.Count != recipe.Length) return false;
        foreach (MagicElement element in recipe)
        {
            int expected = 0, actual = 0;
            foreach (MagicElement value in recipe) if (value == element) expected++;
            for (int i = 0; i < input.Count; i++) if (input[i] == element) actual++;
            if (expected != actual) return false;
        }
        return true;
    }
    public abstract bool ActivateServer(PlayerNetworkCaster caster, Vector3 direction);
}
