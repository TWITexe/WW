using UnityEngine;

// рассчитывает разброс урона и определяет попадание по высоте видимой головы.
public static class SpellDamage
{
    public const float Spread = .1f;
    public const float HeadMultiplier = 1.5f;
    public static bool IsHeadshot(Health target, Collider collider, Vector3 contact)
    {
        if (target.GetComponent<PlayerUltimate>()?.HasForm == true) return false;
        var appearance = target.GetComponent<WizardAppearance>();
        return appearance != null && appearance.HasAnimatedHitboxes
            ? appearance.IsHeadCollider(collider) : IsHeadshot(target, contact);
    }
    // применяем разброс в десять процентов и множитель головы; sample задаёт положение внутри диапазона.
    public static int Roll(int damage, bool headshot, float sample)
    {
        if (damage <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Lerp(1-Spread,1+Spread,Mathf.Clamp01(sample)) * (headshot ? HeadMultiplier : 1)));
    }
    // проверяем высоту контакта относительно геометрии головы и шляпы.
    public static bool IsHeadshot(Health target, Vector3 contact)
    {
        if (target.GetComponent<PlayerUltimate>()?.HasForm == true) return false;
        var appearance=target.GetComponent<WizardAppearance>();
        if(appearance==null||appearance.pieces==null)return false;
        foreach(var piece in appearance.pieces)
        {
            if(piece==null||(piece.name!="Head" && piece.name!="Hat"))continue;
            var renderer=piece.GetComponentInChildren<Renderer>();
            if(renderer!=null && contact.y>=renderer.bounds.min.y-.05f && contact.y<=renderer.bounds.max.y+.05f) return true;
        }
        return false;
    }
}
