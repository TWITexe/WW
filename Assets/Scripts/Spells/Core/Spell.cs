using System.Collections.Generic;
using UnityEngine;

// базовый ассет заклинания: описание, рецепт, перезарядка и точка входа для серверного применения.
public abstract class Spell : ScriptableObject
{
    // выбираем оформление попадания отдельно от механики и рецепта.
    public SpellHitKind hitEffect;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    public string Description => description;
    [SerializeField] private MagicElement[] recipe;
    [SerializeField, Min(0.1f)] protected float cooldown = 1f;
    public float Cooldown => Mathf.Max(0.1f, cooldown);
    public string Name => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public IReadOnlyList<MagicElement> Recipe => recipe;
    // сначала идут рецепты из одной стихии, затем из двух и из трёх; внутри группы — короткая перезарядка.
    public static int CompareSimplicity(Spell left, Spell right)
    {
        int Count(Spell spell)
        {
            int mask=0;
            foreach(var element in spell.Recipe)mask|=1<<(int)element;
            int count=0;while(mask!=0){count+=mask&1;mask>>=1;}return count;
        }
        int result=Count(left).CompareTo(Count(right));
        if(result==0)result=left.Cooldown.CompareTo(right.Cooldown);
        return result!=0?result:string.CompareOrdinal(left.name,right.name);
    }
    // рецепт доступен, только если набор корректен и содержит каждую требуемую стихию.
    public bool IsAvailable(ElementLoadout loadout)
    {
        if (!loadout.IsValid || recipe == null || recipe.Length != 3) return false;
        foreach (MagicElement element in recipe)
            if (!loadout.Contains(element)) return false;
        return true;
    }
    // сравниваем стихии по позициям: перестановка нажатий меняет рецепт.
    public bool MatchesCombo(IReadOnlyList<MagicElement> input)
    {
        if (recipe == null || recipe.Length != 3 || input == null || input.Count != recipe.Length) return false;
        for (int i = 0; i < recipe.Length; i++)
            if (input[i] != recipe[i]) return false;
        return true;
    }
    // наследники создают серверный эффект и возвращают успех, от которого зависит запуск перезарядки.
    public abstract bool ActivateServer(PlayerNetworkCaster caster, Vector3 direction);
}
