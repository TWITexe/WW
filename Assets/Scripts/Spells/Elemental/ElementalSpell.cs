using UnityEngine;

// определяет способ применения стихии: снаряд, наземная зона, вихрь, взрыв вокруг мага или щит.
public enum ElementalCastMode { Bolt, GroundZone, Tornado, SelfBurst, Shield }

// хранит настройки стихийного заклинания отдельно от сетевого объекта его эффекта.
[CreateAssetMenu(menuName = "Spells/Elemental Spell")]
public class ElementalSpell : Spell
{
    public ElementalCastMode mode;
    public GameObject effectPrefab;
    public Color tint = Color.white;
    public float speed = 20;
    public float duration = 4;
    public float radius = 2;
    public int damage = 20;
    public float tickInterval = 0.5f;
    public float knockback;
    public float lift;
    [Range(0.2f, 1f)] public float slow = 1;
    public float slowDuration = 2;
    public int shieldAmount = 50;
    // передаём серверному заклинателю настройки этого ассета и направление применения.
    public override bool ActivateServer(PlayerNetworkCaster caster, Vector3 direction)
        => caster.CastElemental(this, direction);
}
