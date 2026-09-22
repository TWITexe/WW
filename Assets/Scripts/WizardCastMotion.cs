using UnityEngine;

public enum WizardCastGesture : byte { None, Shot, Lob, Area, DelayedStrike, Vortex, Self, Construct }

// Траектории задают положение середины посоха и его поворот в системе координат направления каста.
public static class WizardCastMotion
{
    // При подъёме шляпа отстаёт вниз, при падении — вверх; респавн/телепорт сбрасывают задержку.
    public static float FollowHatHeight(float current, float target, float dt, bool reset,
        float followSpeed, float riseLag, float fallLag)
    {
        if (reset) return target;
        return Mathf.Clamp(Mathf.Lerp(current, target, 1 - Mathf.Exp(-followSpeed * dt)),
            target - riseLag, target + fallLag);
    }

    public static WizardCastGesture ForSpell(Spell spell)
    {
        if (spell is FireBall || spell is WindFlow) return WizardCastGesture.Shot;
        if (spell is ElementalSpell elemental)
        {
            switch (elemental.mode)
            {
                case ElementalCastMode.Bolt: return WizardCastGesture.Shot;
                case ElementalCastMode.GroundZone: return WizardCastGesture.Area;
                case ElementalCastMode.Tornado: return WizardCastGesture.Vortex;
                case ElementalCastMode.SelfBurst:
                case ElementalCastMode.Shield: return WizardCastGesture.Self;
            }
        }
        if (spell is AdvancedSpell advanced)
        {
            if (advanced.IsProjectile) return WizardCastGesture.Lob;
            if (advanced.HasWarning) return WizardCastGesture.DelayedStrike;
            return advanced.kind == AdvancedSpellKind.ThermalSpring ? WizardCastGesture.Area : WizardCastGesture.Construct;
        }
        if (spell is TacticalSpell tactical)
        {
            if (tactical.kind == TacticalKind.SteamDash) return WizardCastGesture.None;
            return tactical.kind == TacticalKind.GravityWell ? WizardCastGesture.Area : WizardCastGesture.Construct;
        }
        return WizardCastGesture.None;
    }

    public static float Duration(WizardCastGesture gesture)
    {
        switch (gesture)
        {
            case WizardCastGesture.Shot: return 1.05f;
            case WizardCastGesture.Lob: return 1.15f;
            case WizardCastGesture.Area: return 1.45f;
            case WizardCastGesture.DelayedStrike: return 1.7f;
            case WizardCastGesture.Vortex: return 1.35f;
            case WizardCastGesture.Self: return 1.5f;
            case WizardCastGesture.Construct: return .75f;
            default: return 0;
        }
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (10 + t * (-15 + 6 * t));
    }

    public static Pose Sample(WizardCastGesture gesture, float time, byte variant, Vector3 rest,
        Quaternion restRotation, Vector3 aim, Vector3? overheadCenter = null)
    {
        float t = Mathf.Clamp01(time);
        if (t <= 0 || t >= 1 || gesture == WizardCastGesture.None) return new Pose(rest, restRotation);
        aim = aim.sqrMagnitude > .0001f ? aim.normalized : Vector3.forward;
        // Сохраняем ориентацию декора вокруг продольной оси, направляя верх посоха к цели.
        Quaternion point = Quaternion.FromToRotation(restRotation * Vector3.up, aim) * restRotation;
        if (gesture == WizardCastGesture.Shot || gesture == WizardCastGesture.Lob)
        {
            float roll = gesture == WizardCastGesture.Lob ? -25 + (variant % 3) * 25 : -135 + (variant % 6) * 54;
            Vector3 planeUp = Vector3.ProjectOnPlane(Vector3.up, aim);
            if (planeUp.sqrMagnitude < .001f) planeUp = Vector3.forward;
            Vector3 side = Quaternion.AngleAxis(roll, aim) * planeUp.normalized;
            Vector3 windup = new Vector3(.25f, .65f, 1.65f) + side * .95f;
            Quaternion windupRotation = Quaternion.AngleAxis(-65, Vector3.Cross(side, aim).normalized) * point;
            Vector3 release = new Vector3(.2f, .55f, 1.9f) + aim * .25f;
            if (t < .26f)
            {
                float s = Ease(t / .26f);
                return new Pose(Bezier(rest, rest + Vector3.forward * .9f, windup + side * .3f, windup, s),
                    Quaternion.Slerp(restRotation, windupRotation, s));
            }
            if (t < .53f)
            {
                float s = Ease((t - .26f) / .27f);
                return new Pose(Bezier(windup, windup + side * .4f + Vector3.forward * .25f,
                    release + side * .35f, release, s), Quaternion.Slerp(windupRotation, point, s));
            }
            float settle = Ease((t - .53f) / .47f);
            Vector3 outside = new Vector3(rest.x < 0 ? -.45f : .45f, .12f, .25f);
            return new Pose(Bezier(release, release + outside, rest + Vector3.forward * .9f + Vector3.up * .12f, rest, settle),
                Quaternion.Slerp(point, restRotation, settle));
        }

        float weight = Ease(t / .16f) * Ease((1 - t) / .32f);
        float u = Mathf.Clamp01((t - .12f) / .56f);
        Vector3 position = rest;
        Quaternion rotation = restRotation;
        switch (gesture)
        {
            case WizardCastGesture.Area:
                // Сначала вертикальный оборот, затем проход справа налево с наклоном.
                float spin = Ease(u / .48f);
                float sweep = Ease((u - .45f) / .55f);
                position = new Vector3(Mathf.Lerp(.95f, -.95f, sweep), .45f + .12f * Mathf.Sin(u * Mathf.PI), .95f);
                rotation = Quaternion.AngleAxis(360 * spin, Vector3.right) *
                    Quaternion.AngleAxis(Mathf.Lerp(-20, 35, sweep), Vector3.forward) * restRotation;
                break;
            case WizardCastGesture.DelayedStrike:
                Vector3 overhead = overheadCenter ?? new Vector3(0, 2.05f, 0);
                Quaternion horizontal = Quaternion.AngleAxis(90, Vector3.forward);
                if (u < .68f)
                {
                    float angle = u / .68f * Mathf.PI * 4;
                    position = overhead;
                    rotation = Quaternion.AngleAxis(-angle * Mathf.Rad2Deg, Vector3.up) * horizontal;
                }
                else
                {
                    float strike = Ease((u - .68f) / .32f);
                    position = Bezier(overhead, overhead + Vector3.forward * .8f,
                        new Vector3(.2f, 1.2f, 1.8f), new Vector3(.2f, .6f, 1.8f), strike);
                    rotation = Quaternion.Slerp(horizontal, point, strike);
                }
                break;
            case WizardCastGesture.Vortex:
                position = new Vector3(0, .6f, 1.1f);
                rotation = Quaternion.AngleAxis(-720 * u, Vector3.forward) * restRotation;
                break;
            case WizardCastGesture.Self:
                float orbit = u * Mathf.PI * 2;
                position = new Vector3(Mathf.Cos(orbit) * 1.25f, .5f + Mathf.Sin(orbit * 2) * .14f, Mathf.Sin(orbit) * 1.25f);
                rotation = Quaternion.Euler(Mathf.Sin(orbit) * 10, 0, -Mathf.Cos(orbit) * 10);
                break;
            case WizardCastGesture.Construct:
                float shake = Mathf.Sin(t * Mathf.PI * 12);
                position = rest + new Vector3(shake * .065f, Mathf.Sin(t * Mathf.PI * 16) * .04f, 0);
                rotation = Quaternion.AngleAxis(shake * 7, Vector3.forward) * restRotation;
                break;
        }
        return Blend(new Pose(rest, restRotation), new Pose(position, rotation), weight);
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float s = 1 - t;
        return s * s * s * a + 3 * s * s * t * b + 3 * s * t * t * c + t * t * t * d;
    }

    // Защищаем весь древок, а не только pivot. Консервативная вертикальная область вокруг тела
    // позволяет широкому замаху и возврату обходить мантию даже при почти вертикальном прицеле.
    public static Pose KeepOutsideBody(Pose pose, float halfLength, float clearance)
    {
        Vector3 axis = pose.rotation * Vector3.up * halfLength;
        Vector3 a = Vector3.ProjectOnPlane(pose.position - axis, Vector3.up);
        Vector3 b = Vector3.ProjectOnPlane(pose.position + axis, Vector3.up);
        Vector3 segment = b - a;
        float t = segment.sqrMagnitude > .000001f ? Mathf.Clamp01(-Vector3.Dot(a, segment) / segment.sqrMagnitude) : 0;
        Vector3 closest = a + segment * t;
        float distance = closest.magnitude;
        if (distance >= clearance) return pose;
        Vector3 outward = closest;
        if (distance < .0001f)
        {
            outward = segment.sqrMagnitude > .000001f ? Vector3.Cross(Vector3.up, segment).normalized : Vector3.forward;
            if (Vector3.Dot(outward, pose.position) < 0) outward = -outward;
        }
        pose.position += outward.normalized * (clearance - distance);
        return pose;
    }

    private static Pose Blend(Pose a, Pose b, float t) =>
        new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));
}
