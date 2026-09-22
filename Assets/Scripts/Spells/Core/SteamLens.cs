using System.Collections.Generic;
using Mirror;
using UnityEngine;

// сервер хранит только действующие линзы; проверка отрезка не пропускает быстрый снаряд между кадрами.
public class SteamLens : NetworkBehaviour
{
    public const int BonusDamage = 6;
    public const float SpeedMultiplier = 1.35f;
    public const float ApertureRadius = 1f;
    private static readonly HashSet<SteamLens> active = new HashSet<SteamLens>();
    private AdvancedSpellEffect effect;

    public override void OnStartServer()
    {
        effect = GetComponent<AdvancedSpellEffect>();
        active.Add(this);
    }

    public override void OnStopServer() => active.Remove(this);

    // выбираем первую пересечённую линзу владельца; снаряд усиливается только один раз за свою жизнь.
    public static bool TryAmplify(uint owner, Vector3 from, Vector3 to, Rigidbody projectile)
    {
        SteamLens nearest = null;
        float nearestFraction = float.MaxValue;
        foreach (var lens in active)
        {
            if (lens == null || lens.effect == null || lens.effect.ownerId != owner || lens.effect.phase != 1 ||
                NetworkTime.time >= lens.effect.phaseStarted + lens.effect.definition.duration) continue;
            if (!Crosses(lens.transform, from, to, out float fraction) || fraction >= nearestFraction) continue;
            nearest = lens;
            nearestFraction = fraction;
        }
        if (nearest == null || projectile == null) return false;
        projectile.linearVelocity *= SpeedMultiplier;
        nearest.effect.phase = 2;
        nearest.effect.phaseStarted = NetworkTime.time;
        return true;
    }

    // пересечение плоскости внутри круглого проёма; простое касание края не расходует способность.
    public static bool Crosses(Transform lens, Vector3 from, Vector3 to, out float fraction)
    {
        Vector3 start = lens.InverseTransformPoint(from);
        Vector3 end = lens.InverseTransformPoint(to);
        fraction = 0;
        float delta = end.z - start.z;
        if (Mathf.Abs(delta) < .00001f) return false;
        fraction = -start.z / delta;
        if (fraction < 0 || fraction > 1) return false;
        Vector3 crossing = Vector3.Lerp(start, end, fraction);
        return crossing.x * crossing.x + crossing.y * crossing.y <= ApertureRadius * ApertureRadius;
    }
}
