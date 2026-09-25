using System;
using UnityEngine;

public enum UltimateKind : byte
{
    PrismaticVolley, PhoenixBirth, SteamFlight, ElementalSpirit, HeatDrain,
    EarthDepths, MirrorLabyrinth, PolarPiercer, GravityInversion, GlacierRam
}

[Serializable]
public class UltimateDefinition
{
    public UltimateKind kind;
    public string title;
    [TextArea(3, 8)] public string description;
    public MagicElement[] elements;
    [Min(0)] public float duration = 10;
    public Color color = Color.cyan;
    public GameObject worldPrefab;
}

[CreateAssetMenu(menuName = "Spells/Ultimate catalog")]
public class UltimateCatalog : ScriptableObject
{
    public const int DamagePerChargePercent = 30;
    public const int FullChargePoints = 100 * DamagePerChargePercent;
    public const double PassiveChargeInterval = 3;
    public const float MirrorPlacementDuration = 45;
    public UltimateDefinition[] definitions = Array.Empty<UltimateDefinition>();
    [Header("Prismatic volley")]
    public const int VolleyCapacity = 4;
    [Range(.1f, 1)] public float copyDamage = .6f;
    [Header("Phoenix")]
    public int meteorHealth = 500;
    public float rebirthDelay = 4;
    [Header("Flight")]
    public float flightSpeed = 10, flightVerticalSpeed = 7, flightHeight = 12;
    [Header("Spirit")]
    public int spiritHealth = 200, spiritStrikeDamage = 30, spiritFrostDamage = 20;
    public float spiritStrikeInterval = 1, spiritFrostInterval = 3;
    [Header("Heat drain")]
    public int drainDamagePerSecond = 20;
    public float drainRange = 22, healingRadius = 6, allyHealing = .5f;
    [Header("Polar piercer")]
    public int laserBodyDamage = 80;
    public float laserHeadMultiplier = 1.5f, laserInterval = .7f, laserRange = 100;
    [Header("World effects")]
    public int mirrorHealth = 90, lavaDamagePerSecond = 12, collapseDamage = 30;
    public float gravityRadius = 12, gravityHeight = 12, slamWarning = .8f;
    public float islandRadius = 3, islandHeight = 5, islandRiseTime = 1.25f;
    public int landingDamage = 40;
    public float orbSpeed = 10, orbRadius = 1.1f, orbBlastRadius = 4, orbStun = 3;
    public float orbJumpSpeed = 8;
    public int orbContactDamage = 15, orbBlastDamage = 40;

    public UltimateDefinition For(ElementLoadout loadout)
    {
        if (!loadout.IsValid) return null;
        int mask = Mask(loadout);
        foreach (var definition in definitions)
            if (definition != null && Mask(definition.elements) == mask) return definition;
        return null;
    }

    public UltimateDefinition Get(UltimateKind kind)
    {
        foreach (var definition in definitions) if (definition != null && definition.kind == kind) return definition;
        return null;
    }
    public static int Mask(ElementLoadout value) => (1 << (int)value.q) | (1 << (int)value.e) | (1 << (int)value.r);
    public static int Mask(MagicElement[] elements)
    {
        int result = 0;
        if (elements != null) foreach (var element in elements) result |= 1 << (int)element;
        return result;
    }
    public static bool IsGroundTargeted(UltimateKind kind) => kind == UltimateKind.MirrorLabyrinth || kind == UltimateKind.GravityInversion;
}
