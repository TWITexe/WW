using System;
using System.Collections.Generic;

// перечисляет стихии; их числовые значения используются в сохранённых рецептах и настройках.
public enum MagicElement { Fire, Air, Ice, Earth, Water }

// хранит три разные стихии и их назначение на клавиши Q, E и R.
[Serializable]
public struct ElementLoadout
{
    public MagicElement q, e, r;
    public static ElementLoadout Default => new ElementLoadout
    { q = MagicElement.Fire, e = MagicElement.Earth, r = MagicElement.Water };
    // возвращаем стихию слота; вызывающий код должен передавать индекс от нуля до двух.
    public MagicElement Get(int slot) => slot == 0 ? q : slot == 1 ? e : r;
    public bool IsValid => Valid(q) && Valid(e) && Valid(r) && q != e && q != r && e != r;
    // проверяем, входит ли значение в допустимый диапазон стихий.
    private static bool Valid(MagicElement value) => value >= MagicElement.Fire && value <= MagicElement.Water;
    // при выборе уже занятой стихии меняем слоты местами, сохраняя уникальность набора.
    public void Assign(int slot, MagicElement element)
    {
        if (slot < 0 || slot > 2 || !Valid(element)) return;
        MagicElement previous = Get(slot);
        for (int i = 0; i < 3; i++)
            if (Get(i) == element) Set(i, previous);
        Set(slot, element);
    }
    // записываем стихию в конкретный слот без дополнительных проверок.
    private void Set(int slot, MagicElement element)
    {
        if (slot == 0) q = element;
        else if (slot == 1) e = element;
        else r = element;
    }
    // проверяем, присутствует ли стихия среди трёх выбранных.
    public bool Contains(MagicElement element) => q == element || e == element || r == element;
    // находим клавишу стихии в текущем наборе; отсутствующую обозначаем вопросительным знаком.
    public string KeyFor(MagicElement element) => q == element ? "Q" : e == element ? "E" : r == element ? "R" : "?";
    // переводим рецепт из стихий в подпись клавиш с учётом текущих назначений.
    public string KeysFor(IReadOnlyList<MagicElement> recipe)
    {
        var keys = new string[recipe.Count];
        for (int i = 0; i < recipe.Count; i++) keys[i] = KeyFor(recipe[i]);
        return string.Join(" → ", keys);
    }
    // возвращаем русское название стихии для интерфейса.
    public static string Label(MagicElement element) => element == MagicElement.Fire ? "Огонь" :
        element == MagicElement.Air ? "Воздух" : element == MagicElement.Ice ? "Лёд" : element == MagicElement.Earth ? "Земля" : "Вода";
}
