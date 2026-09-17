using System;
using System.Collections.Generic;

public enum MagicElement { Fire, Air, Ice, Earth, Water }

[Serializable]
public struct ElementLoadout
{
    public MagicElement q, e, r;
    public static ElementLoadout Default => new ElementLoadout
    { q = MagicElement.Fire, e = MagicElement.Air, r = MagicElement.Ice };
    public MagicElement Get(int slot) => slot == 0 ? q : slot == 1 ? e : r;
    public bool IsValid => Valid(q) && Valid(e) && Valid(r) && q != e && q != r && e != r;
    private static bool Valid(MagicElement value) => value >= MagicElement.Fire && value <= MagicElement.Water;
    public void Assign(int slot, MagicElement element)
    {
        if (slot < 0 || slot > 2 || !Valid(element)) return;
        MagicElement previous = Get(slot);
        for (int i = 0; i < 3; i++)
            if (Get(i) == element) Set(i, previous);
        Set(slot, element);
    }
    private void Set(int slot, MagicElement element)
    {
        if (slot == 0) q = element;
        else if (slot == 1) e = element;
        else r = element;
    }
    public bool Contains(MagicElement element) => q == element || e == element || r == element;
    public string KeyFor(MagicElement element) => q == element ? "Q" : e == element ? "E" : r == element ? "R" : "?";
    public string KeysFor(IReadOnlyList<MagicElement> recipe)
    {
        var keys = new string[recipe.Count];
        for (int i = 0; i < recipe.Count; i++) keys[i] = KeyFor(recipe[i]);
        return string.Join(" → ", keys);
    }
    public static string Label(MagicElement element) => element == MagicElement.Fire ? "Огонь" :
        element == MagicElement.Air ? "Воздух" : element == MagicElement.Ice ? "Лёд" : element == MagicElement.Earth ? "Земля" : "Вода";
}
