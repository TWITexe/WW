using UnityEngine;

// перечисляет тактические механики, выбираемые настройками заклинания.
public enum TacticalKind { SteamDash, IceMirror, StoneWall, FireSeal, GravityWell, SnowDecoy }
// хранит рецепт и параметры тактического эффекта: тип, длительность, радиус и урон.
[CreateAssetMenu(menuName="Spells/Tactical spell")]
public class TacticalSpell : Spell
{
    public TacticalKind kind;
    public GameObject effectPrefab;
    public Color tint = Color.cyan;
    public float duration = 4;
    public float radius = 3;
    public int damage;
    // передаём серверному заклинателю настройки выбранной тактической способности.
    public override bool ActivateServer(PlayerNetworkCaster caster, Vector3 direction) => caster.CastTactical(this, direction);
}
