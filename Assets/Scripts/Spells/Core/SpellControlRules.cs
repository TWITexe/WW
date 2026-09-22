using System;
using UnityEngine;

// общие правила контроля; сервер и предсказание читают одинаковые сроки замедлений.
public static class SpellControlRules
{
    public const double PeriodicLiftInterval = 1.25;

    public struct TimedSlow
    {
        public float multiplier;
        public double until;
    }

    // выбираем только сильнейшее из ещё действующих замедлений, без перемножения.
    public static float SlowMultiplier(TimedSlow[] slows, double now)
    {
        float result = 1;
        if (slows != null)
            foreach (var slow in slows)
                if (slow.until > now) result = Mathf.Min(result, slow.multiplier);
        return result;
    }

    // новый массив создаётся только при попадании; снимки движения могут безопасно хранить старый.
    public static TimedSlow[] AddSlow(TimedSlow[] slows, float multiplier, float duration, double now)
    {
        slows = slows ?? Array.Empty<TimedSlow>();
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) ||
            float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0) return slows;
        multiplier = Mathf.Clamp(multiplier, .2f, 1);
        if (multiplier >= 1) return slows;
        double until = now + duration;
        int count = 1;
        foreach (var slow in slows)
        {
            if (slow.until <= now) continue;
            if (slow.multiplier == multiplier) until = Math.Max(until, slow.until);
            else count++;
        }
        var result = new TimedSlow[count];
        int index = 0;
        foreach (var slow in slows)
            if (slow.until > now && slow.multiplier != multiplier) result[index++] = slow;
        result[index] = new TimedSlow { multiplier = multiplier, until = until };
        return result;
    }

    // ограничение принадлежит цели, поэтому несколько вихрей не подбрасывают её каждый тик.
    public static Vector3 LimitPeriodicLift(Vector3 force, double now, ref double nextLiftAt)
    {
        if (force.y <= 0) return force;
        if (now < nextLiftAt) force.y = 0;
        else nextLiftAt = now + PeriodicLiftInterval;
        return force;
    }
}
