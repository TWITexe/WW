using Mirror;
using UnityEngine;

public class UltimateMirror : MonoBehaviour
{
    public UltimateWorldEffect effect;
    public int index;
    public void ReceiveDamage(int damage)
    {
        if (effect != null && effect.isServer) effect.DamageMirror(index, damage);
    }
    public static bool TryReflect(Collider hit, Transform projectile, uint incomingOwner, out uint reflectedOwner)
    {
        reflectedOwner = incomingOwner;
        var panel = hit.GetComponent<UltimateMirror>();
        if (panel == null || panel.effect == null || !panel.effect.isServer || !panel.gameObject.activeInHierarchy) return false;
        var body = projectile.GetComponent<Rigidbody>();
        var advanced = projectile.GetComponent<AdvancedSpellEffect>();
        if (body == null) return false;
        var history = projectile.GetComponent<UltimateReflectionHistory>() ?? projectile.gameObject.AddComponent<UltimateReflectionHistory>();
        if (history.bounces >= 8) { panel.ReceiveDamage(20); return false; }
        if (history.lastPanel == panel && NetworkTime.time - history.lastAt < .12) return true;
        Vector3 velocity = advanced != null ? advanced.velocity : body.linearVelocity;
        Vector3 normal = panel.transform.forward;
        Vector3 direction = Vector3.Reflect(velocity.normalized, normal).normalized;
        if (direction.sqrMagnitude < .001f) direction = normal;
        if (advanced != null) advanced.velocity = direction * velocity.magnitude;
        else body.linearVelocity = direction * velocity.magnitude;
        projectile.rotation = Quaternion.LookRotation(direction);
        float radius = PlayerNetworkCaster.ProjectileHalfSize(projectile.gameObject).magnitude;
        float side = Vector3.Dot(direction, normal) >= 0 ? 1 : -1;
        float depth = Vector3.Dot(projectile.position - panel.transform.position, normal);
        projectile.position += normal * (side * (radius + .2f) - depth);
        history.lastPanel = panel; history.lastAt = NetworkTime.time; history.bounces++;
        int damage = projectile.TryGetComponent<FireballProjectile>(out var fire) ? fire.ImpactDamage :
            projectile.TryGetComponent<ElementalEffect>(out var elemental) ? elemental.ImpactDamage : 20;
        panel.ReceiveDamage(damage);
        return true;
    }
}

public class UltimateReflectionHistory : MonoBehaviour
{
    public UltimateMirror lastPanel;
    public double lastAt;
    public int bounces;
}
