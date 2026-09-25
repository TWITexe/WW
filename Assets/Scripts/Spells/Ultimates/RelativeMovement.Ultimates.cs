using Mirror;
using UnityEngine;

// The same state travels in prediction snapshots, including the flight ceiling and field expiry.
public partial class RelativeMovement
{
    public struct UltimateMotionState
    {
        public double flightUntil, rootUntil, spiritUntil, gravityUntil;
        public float ceiling, horizontalSpeed, verticalSpeed, hoverY, launchY;
        public double launchUntil;
        public bool frozen;
    }
    [SyncVar] private UltimateMotionState ultimateMotion;
    private int slamDamage;
    private uint slamOwner;
    private double slamUntil;
    private float baseControllerHeight, baseControllerRadius;
    private Vector3 baseControllerCenter;
    public bool IsFlying => NetworkTime.time < ultimateMotion.flightUntil;
    public bool IsUltimateRooted => NetworkTime.time < ultimateMotion.rootUntil;

    [Server] public void ServerFlight(float duration, float height, float speed, float vertical)
    {
        ultimateMotion.flightUntil = NetworkTime.time + duration;
        ultimateMotion.ceiling = transform.position.y + height;
        ultimateMotion.horizontalSpeed = speed;
        ultimateMotion.verticalSpeed = vertical;
        ultimateMotion.launchY = transform.position.y + controller.height * transform.lossyScale.y;
        ultimateMotion.launchUntil = NetworkTime.time + .3;
        vertSpeed = 0; dashRemaining = 0; iceVelocity = Vector3.zero;
    }
    [Server] public void ServerSpirit(float duration) => ultimateMotion.spiritUntil = NetworkTime.time + duration;
    [Server] public void ServerRoot(float duration, bool frozen)
    {
        ultimateMotion.rootUntil = NetworkTime.time + duration;
        ultimateMotion.frozen = frozen;
        externalVelocity = Vector3.zero; dashRemaining = 0; iceVelocity = Vector3.zero;
        if (frozen) vertSpeed = 0;
    }
    [Server] public void ServerEndUltimateMotion()
    {
        ultimateMotion.flightUntil = ultimateMotion.rootUntil = ultimateMotion.spiritUntil = 0;
        ultimateMotion.frozen = false;
    }
    [Server] public void ServerGravityField(float hoverY)
    {
        if (health != null && (health.IsDead || health.GetComponent<PlayerUltimate>()?.IsMeteor == true)) return;
        ultimateMotion.gravityUntil = NetworkTime.time + .2;
        ultimateMotion.hoverY = hoverY;
    }
    [Server] public void ServerSlam(int damage, uint owner)
    {
        if (health == null || health.IsDead || health.GetComponent<PlayerUltimate>()?.IsMeteor == true) return;
        ultimateMotion.gravityUntil = 0;
        ultimateMotion.flightUntil = 0;
        vertSpeed = -22;
        slamDamage = Mathf.Max(slamDamage, damage);
        slamOwner = owner; slamUntil = NetworkTime.time + 5;
        health.RecordAttacker(owner);
    }
    void CheckUltimateLanding(bool grounded, double now)
    {
        if (slamDamage <= 0) return;
        if (now > slamUntil) { slamDamage = 0; return; }
        if (!grounded) return;
        int damage = slamDamage; slamDamage = 0;
        health.TakeDamage(damage, slamOwner);
        Vector3 feet = transform.TransformPoint(controller.center) - Vector3.up * controller.height * transform.lossyScale.y * .5f;
        RpcUltimateLanding(feet);
    }
    [ClientRpc] void RpcUltimateLanding(Vector3 position)
        => SpellVfx.Impact(position, new Color(.6f, .9f, 1), 1.8f, SpellHitKind.Stone);

    bool SimulateUltimate(ref MoveInput input, float dt, double now)
    {
        if (baseControllerHeight <= 0)
        {
            baseControllerHeight = controller.height; baseControllerRadius = controller.radius;
            baseControllerCenter = controller.center;
        }
        bool spirit = now < ultimateMotion.spiritUntil;
        float height = spirit ? Mathf.Max(3.5f, baseControllerHeight) : baseControllerHeight;
        // Changing these properties recreates the native controller: only resize at form transitions.
        float radius = spirit ? Mathf.Max(.8f, baseControllerRadius) : baseControllerRadius;
        Vector3 center = baseControllerCenter + Vector3.up * ((height - baseControllerHeight) * .5f);
        if (!Mathf.Approximately(controller.height, height)) controller.height = height;
        if (!Mathf.Approximately(controller.radius, radius)) controller.radius = radius;
        if (controller.center != center) controller.center = center;
        if (vertSpeed <= 0 && now >= ultimateMotion.flightUntil)
        {
            Vector3 feet = transform.TransformPoint(controller.center) - Vector3.up * controller.height * transform.lossyScale.y * .5f;
            Vector3 carry = UltimateWorldEffect.PlatformDisplacement(feet, dt, now);
            if (carry.sqrMagnitude > 0) controller.Move(carry);
        }
        if (now < ultimateMotion.rootUntil)
        {
            input.blocked = true; input.jump = false; input.movement = Vector3.zero;
            dashRemaining = 0; externalVelocity = Vector3.zero; iceVelocity = Vector3.zero;
            if (ultimateMotion.frozen) { vertSpeed = 0; sprinting = false; return true; }
        }
        if (now >= ultimateMotion.flightUntil || now < stunUntil) return false;
        bool blocked = input.blocked;
        Vector3 planar = blocked ? Vector3.zero : Vector3.ClampMagnitude(input.movement, 1);
        float vertical = blocked ? 0 : Mathf.Clamp(input.vertical, -1, 1) * ultimateMotion.verticalSpeed;
        if (now < ultimateMotion.launchUntil)
            vertical = Mathf.Max(vertical, Mathf.Clamp((ultimateMotion.launchY - transform.position.y) / dt, 0, 14));
        if (vertical > 0) vertical = Mathf.Min(vertical, Mathf.Max(0, ultimateMotion.ceiling - transform.position.y) / dt);
        float slow = SpellControlRules.SlowMultiplier(slows, now);
        if (!blocked && input.forward.sqrMagnitude > .01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(input.forward), 1 - Mathf.Exp(-rotSpeed * dt));
        externalVelocity = Vector3.Lerp(externalVelocity, Vector3.zero, 5 * dt);
        controller.Move((planar * ultimateMotion.horizontalSpeed * slow + Vector3.up * vertical + externalVelocity) * dt);
        vertSpeed = vertical; isJumping = false; sprinting = false;
        return true;
    }
}
