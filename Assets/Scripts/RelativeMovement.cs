using Mirror;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class RelativeMovement : NetworkBehaviour
{
    [SerializeField] CameraShake cameraShake;
    [SerializeField] Camera playerCamera;
    [SerializeField] float rotSpeed = 8;
    public Camera ViewCamera => playerCamera;
    [SerializeField] float moveSpeed = 6;
    [SerializeField] float jumpSpeed = 15;
    [SerializeField] float gravity = -9.8f;
    [SerializeField] float terminalVelocity = -22;
    [SerializeField, Min(1)] float fallGravityMultiplier = 1.8f;
    [SerializeField] float minFall = -1.5f;
    [SerializeField] float groundProbeDistance = .10f;
    [SerializeField] float slowAfterJumpTime = .3f;
    [SerializeField] float speedMultiplier = .5f;
    [SerializeField] float jumpCooldown = 1;
    [SyncVar] private float slowMultiplier = 1;
    private double slowUntil;
    private float jumpCooldownTimer, jumpSlowTimer, vertSpeed;
    private bool isJumping;
    private CharacterController controller;
    private Health health;
    private Vector3 externalVelocity;
    private Vector3 dashVelocity;
    private float dashRemaining;
    public Vector3 PlanarInputDirection { get; private set; }

    private void Awake()
    {
        controller = GetComponent<CharacterController>(); health = GetComponent<Health>(); vertSpeed = minFall;
        // Disable remote cameras before Mirror's client callbacks and the first render.
        SetLocalCamera(false);
    }
    private void SetLocalCamera(bool active)
    {
        if (playerCamera != null) playerCamera.gameObject.SetActive(active);
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        SetLocalCamera(isLocalPlayer);
    }
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        SetLocalCamera(true);
    }
    public override void OnStopLocalPlayer()
    {
        SetLocalCamera(false);
        base.OnStopLocalPlayer();
    }
    private void Update()
    {
        if (isServer && NetworkTime.time >= slowUntil) slowMultiplier = 1;
        if (!isOwned || playerCamera == null || !controller.enabled || (health != null && health.IsDead)) return;
        jumpCooldownTimer = Mathf.Max(0, jumpCooldownTimer - Time.deltaTime);
        jumpSlowTimer = Mathf.Max(0, jumpSlowTimer - Time.deltaTime);
        bool blocked = PlayerGameUI.InputBlocked;
        bool grounded = vertSpeed <= 0 && ProbeGround();
        bool jumped = false;
        if (grounded)
        {
            if (isJumping) { isJumping = false; jumpSlowTimer = slowAfterJumpTime; }
            vertSpeed = minFall;
            if (!blocked && Input.GetButtonDown("Jump") && jumpCooldownTimer <= 0)
            {
                vertSpeed = jumpSpeed; jumpCooldownTimer = jumpCooldown; isJumping = true; jumped = true;
            }
        }
        if (!grounded && !jumped)
            // Keep the original jump ascent. Descent has its own multiplier,
            // rather than multiplying the jump's 5x gravity a second time.
            vertSpeed = Mathf.Max(terminalVelocity, vertSpeed + gravity * (vertSpeed < 0 ? fallGravityMultiplier : 5) * Time.deltaTime);
        Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized;
        Vector3 movement = blocked ? Vector3.zero :
            (right * Input.GetAxis("Horizontal") + forward * Input.GetAxis("Vertical")).normalized;
        PlanarInputDirection = movement;
        float multiplier = slowMultiplier;
        if (!blocked && Input.GetKey(KeyCode.LeftShift)) multiplier *= 1.5f;
        if (isJumping) multiplier *= speedMultiplier;
        if (jumpSlowTimer > 0) multiplier *= speedMultiplier;
        movement *= moveSpeed * multiplier;
        if (!blocked && forward.sqrMagnitude > .01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forward), 1 - Mathf.Exp(-rotSpeed * Time.deltaTime));
        movement.y = vertSpeed;
        externalVelocity = Vector3.Lerp(externalVelocity, Vector3.zero, 5 * Time.deltaTime);
        float dashStep = Mathf.Min(Time.deltaTime, dashRemaining);
        dashRemaining = Mathf.Max(0, dashRemaining - Time.deltaTime);
        controller.Move((movement + externalVelocity) * Time.deltaTime + dashVelocity * dashStep);
        cameraShake?.SetShaking(!blocked && grounded && movement.x * movement.x + movement.z * movement.z > .1f, movement.sqrMagnitude);
    }
    public bool ProbeGround()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        float radius = controller.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * .9f;
        Vector3 feet = transform.TransformPoint(controller.center) - Vector3.up * controller.height * transform.lossyScale.y * .5f;
        const float lift = .08f;
        var hits = Physics.SphereCastAll(feet + Vector3.up * (radius + lift), radius, Vector3.down,
            lift + groundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
            if (!hit.collider.transform.IsChildOf(transform.root) &&
                Vector3.Dot(hit.normal, Vector3.up) >= Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad))
                return true;
        return false;
    }
    public void AddExternalForce(Vector3 force) => externalVelocity += force;
    [Server] public void ServerDash(Vector3 direction) => TargetDash(connectionToClient, direction);
    [TargetRpc] private void TargetDash(NetworkConnectionToClient target, Vector3 direction)
    {
        if (health != null && health.IsDead) return;
        dashVelocity = Vector3.ProjectOnPlane(direction,Vector3.up).normalized * 24;
        dashRemaining = .22f;
    }
    [Server] public void ApplySlow(float multiplier, float duration)
    {
        slowMultiplier = Mathf.Min(slowMultiplier, Mathf.Clamp(multiplier, .2f, 1));
        slowUntil = System.Math.Max(slowUntil, NetworkTime.time + duration);
    }
    [Server] public void ServerAddExternalForce(Vector3 force) => TargetAddExternalForce(connectionToClient, force);
    [TargetRpc] private void TargetAddExternalForce(NetworkConnectionToClient target, Vector3 force) => AddExternalForce(force);
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForce(new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z) * 5, ForceMode.Impulse);
    }
    public void ResetVerticalVelocity()
    {
        vertSpeed = minFall; externalVelocity = Vector3.zero; dashVelocity=Vector3.zero;dashRemaining=0;PlanarInputDirection=Vector3.zero; isJumping = false; jumpCooldownTimer = jumpSlowTimer = 0;
        if (isServer) { slowMultiplier = 1; slowUntil = 0; }
    }
    public void ForceGroundReset() => vertSpeed = minFall;
}
