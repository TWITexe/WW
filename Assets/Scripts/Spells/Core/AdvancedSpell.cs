using UnityEngine;

// отдельные тройные рецепты меняют только один набор стихий.
public enum AdvancedSpellKind { ShardVortex, ScaldingMist, Meteor, ThermalSpring, BoilingIce, IceBridge, CrystalCrash, SteamLens }

[CreateAssetMenu(menuName = "Spells/Advanced spell")]
public class AdvancedSpell : Spell
{
    public AdvancedSpellKind kind;
    public GameObject effectPrefab;
    public Color tint = Color.cyan;
    public float radius = 3, delay, duration = 3, tickInterval = 1;
    public int impactDamage, tickDamage, healPerTick;
    [Range(.2f, 1)] public float slow = 1;
    public float slowDuration = 2, stunDuration;
    public float projectileSpeed = 14, projectileGravity = 9;
    public float impactDelay;
    // моменты урона относительно запуска импортированного эффекта взрыва.
    public float[] impactOffsets = new float[0];
    public Vector3 bridgeSize = new Vector3(3, .2f, 10);
    public bool IsProjectile => kind == AdvancedSpellKind.ScaldingMist || kind == AdvancedSpellKind.BoilingIce;
    public bool HasWarning => kind == AdvancedSpellKind.ShardVortex || kind == AdvancedSpellKind.Meteor || kind == AdvancedSpellKind.CrystalCrash;
    public override bool ActivateServer(PlayerNetworkCaster caster, Vector3 direction) => caster.CastAdvanced(this, direction);
}
