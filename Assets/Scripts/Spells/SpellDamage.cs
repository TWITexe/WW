using UnityEngine;

public static class SpellDamage
{
    public const float Spread = .1f;
    public const float HeadMultiplier = 1.5f;
    public static int Roll(int damage, bool headshot, float sample)
    {
        if (damage <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Lerp(1-Spread,1+Spread,Mathf.Clamp01(sample)) * (headshot ? HeadMultiplier : 1)));
    }
    // The contact height is compared with the visible head, excluding the hat.
    public static bool IsHeadshot(Health target, Vector3 contact)
    {
        var appearance=target.GetComponent<WizardAppearance>();
        if(appearance==null||appearance.pieces==null)return false;
        foreach(var piece in appearance.pieces)
        {
            if(piece==null||piece.name!="Head")continue;
            var renderer=piece.GetComponentInChildren<Renderer>();
            if(renderer!=null)return contact.y>=renderer.bounds.min.y && contact.y<=renderer.bounds.max.y+.05f;
        }
        return false;
    }
}
