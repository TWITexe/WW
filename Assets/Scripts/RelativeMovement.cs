using System.Collections.Generic;
using Mirror;
using UnityEngine;

// сервер и клиентское предсказание используют общий расчёт движения с фиксированным шагом.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(CharacterController))]
public class RelativeMovement : NetworkBehaviour
{
    [SerializeField] CameraShake cameraShake;
    [SerializeField] Camera playerCamera;
    [SerializeField] float rotSpeed = 8;
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

    // клиент передаёт только ввод; позицию, скорость, длительность шага и силу эффектов определяет сервер.
    public struct MoveInput
    {
        public uint epoch, sequence;
        public Vector3 movement, forward;
        public bool jump, sprint, blocked;
    }
    public struct MotorState
    {
        public uint epoch, tick, acknowledged;
        public Vector3 position, externalVelocity, dashVelocity;
        public Quaternion rotation;
        public float verticalSpeed, jumpCooldown, jumpSlow, dashRemaining, slowMultiplier;
        public double slowUntil, simulationTime;
        public bool jumping, dead;
    }

    const int MaxServerQueue = 8;
    const int MaxPredictionHistory = 128;
    const double InputTimeout = .15;
    readonly Queue<MoveInput> serverInputs = new Queue<MoveInput>();
    readonly List<MoveInput> pendingInputs = new List<MoveInput>();
    uint serverEpoch = 1, serverTick, lastReceived, lastProcessed;
    uint clientEpoch, clientSequence, lastSnapshotTick;
    double lastArrival, tokenTime;
    float inputTokens = 12;
    MoveInput sampledInput, lastServerInput;
    bool jumpQueued, serverDead;
    float slowMultiplier = 1;
    double slowUntil;
    float jumpCooldownTimer, jumpSlowTimer, vertSpeed;
    bool isJumping;
    CharacterController controller;
    Health health;
    Vector3 externalVelocity, dashVelocity;
    float dashRemaining;
    Transform visualRoot;
    Vector3 visualRestPosition, correctionOffset, previousTickPosition;
    bool hasPreviousTick;

    public Camera ViewCamera => playerCamera;
    public Vector3 PlanarInputDirection { get; private set; }
    // сглаживаем отображение между физическими шагами, не смещая серверный коллайдер.
    public Vector3 PresentationOffset
    {
        get
        {
            float fraction = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            return correctionOffset + (hasPreviousTick && isLocalPlayer
                ? (previousTickPosition - transform.position) * (1 - fraction) : Vector3.zero);
        }
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        vertSpeed = minFall;
        var appearance = GetComponent<WizardAppearance>();
        visualRoot = appearance != null ? appearance.visualRoot : null;
        if (visualRoot != null) visualRestPosition = visualRoot.localPosition;
        SetLocalCamera(false);
    }
    void SetLocalCamera(bool active)
    {
        if (playerCamera != null) playerCamera.gameObject.SetActive(active);
    }
    public override void OnStartServer()
    {
        tokenTime = NetworkTime.localTime;
        var sync = GetComponent<NetworkTransformBase>();
        if (sync != null) sync.syncDirection = SyncDirection.ServerToClient;
    }
    public override void OnStartClient() => SetLocalCamera(isLocalPlayer);
    public override void OnStartLocalPlayer() => SetLocalCamera(true);
    public override void OnStopLocalPlayer()
    {
        SetLocalCamera(false);
        pendingInputs.Clear();
    }

    // сохраняем короткое нажатие прыжка до ближайшего фиксированного шага.
    void Update()
    {
        if (!isLocalPlayer) return;
        correctionOffset = Vector3.Lerp(correctionOffset, Vector3.zero, 1 - Mathf.Exp(-18 * Time.deltaTime));
        bool blocked = PlayerGameUI.InputBlocked || (health != null && health.IsDead);
        Vector3 forward = playerCamera != null
            ? Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized : transform.forward;
        Vector3 right = playerCamera != null
            ? Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized : transform.right;
        sampledInput = new MoveInput
        {
            forward = forward,
            movement = blocked ? Vector3.zero : (right * Input.GetAxis("Horizontal") + forward * Input.GetAxis("Vertical")).normalized,
            sprint = !blocked && Input.GetKey(KeyCode.LeftShift),
            blocked = blocked
        };
        if (blocked) jumpQueued = false;
        else jumpQueued |= Input.GetButtonDown("Jump");
        PlanarInputDirection = sampledInput.movement;
        cameraShake?.SetShaking(!blocked && ProbeGround() && PlanarInputDirection.sqrMagnitude > .1f,
            moveSpeed * moveSpeed * (sampledInput.sprint ? 2.25f : 1) * slowMultiplier * slowMultiplier);
    }
    void LateUpdate()
    {
        if (isLocalPlayer && visualRoot != null)
            visualRoot.localPosition = visualRestPosition + visualRoot.parent.InverseTransformVector(PresentationOffset);
    }

    void FixedUpdate()
    {
        if (isLocalPlayer) { previousTickPosition = transform.position; hasPreviousTick = true; }
        if (isLocalPlayer && (isServer || clientEpoch != 0))
        {
            MoveInput input = sampledInput;
            input.epoch = isServer ? serverEpoch : clientEpoch;
            input.sequence = ++clientSequence;
            input.jump = jumpQueued;
            jumpQueued = false;
            CmdMove(input);
            // хост выполняет только серверный расчёт, без повторного предсказания.
            if (!isServer && !serverDead && (health == null || !health.IsDead))
            {
                if (pendingInputs.Count == MaxPredictionHistory) pendingInputs.RemoveAt(0);
                pendingInputs.Add(input);
                Simulate(input, Time.fixedDeltaTime, NetworkTime.predictedTime);
            }
        }
        if (isServer) ServerStep();
    }

    [Command]
    void CmdMove(MoveInput input) => ReceiveInput(input);

    // приём пакета только добавляет ввод в очередь; частые команды не ускоряют симуляцию.
    bool ReceiveInput(MoveInput input)
    {
        double now = NetworkTime.localTime;
        inputTokens = Mathf.Min(12, inputTokens + (float)(now - tokenTime) * 75);
        tokenTime = now;
        if (inputTokens < 1) return false;
        inputTokens--;
        if (input.epoch != serverEpoch || input.sequence <= lastReceived ||
            !Finite(input.movement) || !Finite(input.forward) ||
            input.movement.sqrMagnitude > 1.01f || input.forward.sqrMagnitude > 1.01f ||
            Mathf.Abs(input.movement.y) > .001f || Mathf.Abs(input.forward.y) > .001f)
            return false;
        lastReceived = input.sequence;
        lastArrival = now;
        if (serverInputs.Count == MaxServerQueue) serverInputs.Dequeue();
        serverInputs.Enqueue(input);
        return true;
    }
    void ServerStep()
    {
        serverTick++;
        MoveInput input;
        if (serverInputs.Count > 0)
        {
            input = serverInputs.Dequeue();
            lastProcessed = input.sequence;
            lastServerInput = input;
        }
        else
        {
            input = lastServerInput;
            input.jump = false;
            if (NetworkTime.localTime - lastArrival > InputTimeout)
            {
                input.movement = Vector3.zero;
                input.sprint = false;
                input.blocked = true;
            }
        }
        Simulate(input, Time.fixedDeltaTime, NetworkTime.time);
        if (connectionToClient != null) TargetMovementState(CaptureState());
    }

    // общий расчёт для серверного шага и повтора ещё не подтверждённого ввода.
    void Simulate(MoveInput input, float dt, double simulationTime)
    {
        if (!controller.enabled || (health != null && health.IsDead)) return;
        if (simulationTime >= slowUntil) slowMultiplier = 1;
        jumpCooldownTimer = Mathf.Max(0, jumpCooldownTimer - dt);
        jumpSlowTimer = Mathf.Max(0, jumpSlowTimer - dt);
        bool grounded = vertSpeed <= 0 && ProbeGround();
        bool jumped = false;
        if (grounded)
        {
            if (isJumping) { isJumping = false; jumpSlowTimer = slowAfterJumpTime; }
            vertSpeed = minFall;
            if (!input.blocked && input.jump && jumpCooldownTimer <= 0)
            {
                vertSpeed = jumpSpeed;
                jumpCooldownTimer = jumpCooldown;
                isJumping = true;
                jumped = true;
            }
        }
        if (!grounded && !jumped)
            vertSpeed = Mathf.Max(terminalVelocity, vertSpeed + gravity * (vertSpeed < 0 ? fallGravityMultiplier : 5) * dt);
        Vector3 movement = input.blocked ? Vector3.zero : Vector3.ClampMagnitude(input.movement, 1);
        float multiplier = slowMultiplier;
        if (!input.blocked && input.sprint) multiplier *= 1.5f;
        if (isJumping) multiplier *= speedMultiplier;
        if (jumpSlowTimer > 0) multiplier *= speedMultiplier;
        movement *= moveSpeed * multiplier;
        if (!input.blocked && input.forward.sqrMagnitude > .01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(input.forward), 1 - Mathf.Exp(-rotSpeed * dt));
        movement.y = vertSpeed;
        externalVelocity = Vector3.Lerp(externalVelocity, Vector3.zero, 5 * dt);
        float dashStep = Mathf.Min(dt, dashRemaining);
        dashRemaining = Mathf.Max(0, dashRemaining - dt);
        controller.Move((movement + externalVelocity) * dt + dashVelocity * dashStep);
    }

    MotorState CaptureState() => new MotorState
    {
        epoch = serverEpoch, tick = serverTick, acknowledged = lastProcessed,
        position = transform.position, rotation = transform.rotation,
        verticalSpeed = vertSpeed, externalVelocity = externalVelocity,
        dashVelocity = dashVelocity, dashRemaining = dashRemaining,
        jumpCooldown = jumpCooldownTimer, jumpSlow = jumpSlowTimer, jumping = isJumping,
        slowMultiplier = slowMultiplier, slowUntil = slowUntil, simulationTime = NetworkTime.time,
        dead = health != null && health.IsDead
    };

    [TargetRpc(channel = Channels.Unreliable)]
    void TargetMovementState(MotorState state)
    {
        if (!isServer) Reconcile(state);
    }
    void Reconcile(MotorState state)
    {
        if (state.epoch < clientEpoch || (state.epoch == clientEpoch && state.tick <= lastSnapshotTick)) return;
        bool reset = state.epoch != clientEpoch;
        Vector3 oldPosition = transform.position;
        Vector3 displayedPosition = oldPosition + correctionOffset;
        clientEpoch = state.epoch;
        lastSnapshotTick = state.tick;
        serverDead = state.dead;
        if (reset)
        {
            pendingInputs.Clear();
            clientSequence = 0;
            jumpQueued = false;
        }
        else pendingInputs.RemoveAll(input => input.sequence <= state.acknowledged);
        SetPose(state.position, state.rotation);
        vertSpeed = state.verticalSpeed;
        externalVelocity = state.externalVelocity;
        dashVelocity = state.dashVelocity;
        dashRemaining = state.dashRemaining;
        jumpCooldownTimer = state.jumpCooldown;
        jumpSlowTimer = state.jumpSlow;
        isJumping = state.jumping;
        slowMultiplier = state.slowMultiplier;
        slowUntil = state.slowUntil;
        if (!state.dead)
            for (int i = 0; i < pendingInputs.Count; i++)
                Simulate(pendingInputs[i], Time.fixedDeltaTime, state.simulationTime + (i + 1) * Time.fixedDeltaTime);
        previousTickPosition += transform.position - oldPosition;
        if (reset) hasPreviousTick = false;
        Vector3 error = displayedPosition - transform.position;
        correctionOffset = reset || state.dead || error.sqrMagnitude > 4 ? Vector3.zero : error;
    }
    void SetPose(Vector3 position, Quaternion rotation)
    {
        bool enabledBefore = controller.enabled;
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = enabledBefore;
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
                Vector3.Dot(hit.normal, Vector3.up) >= Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad)) return true;
        return false;
    }
    static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

    [Server] public void AddExternalForce(Vector3 force)
    {
        if (Finite(force) && (health == null || !health.IsDead)) externalVelocity += force;
    }
    [Server] public void ServerAddExternalForce(Vector3 force) => AddExternalForce(force);
    [Server] public void ServerDash(Vector3 direction)
    {
        if (!Finite(direction) || (health != null && health.IsDead)) return;
        dashVelocity = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * 24;
        dashRemaining = .22f;
    }
    [Server] public void ApplySlow(float multiplier, float duration)
    {
        slowMultiplier = Mathf.Min(slowMultiplier, Mathf.Clamp(multiplier, .2f, 1));
        slowUntil = System.Math.Max(slowUntil, NetworkTime.time + duration);
    }
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // повтор предсказания не должен повторно толкать физические объекты.
        if (isServer && hit.rigidbody != null && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForce(new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z) * 5, ForceMode.Impulse);
    }

    [Server] public void ServerTeleport(Vector3 position, Quaternion rotation)
    {
        SetPose(position, rotation);
        ResetVerticalVelocity();
        GetComponent<PlayerServerTransform>()?.ServerTeleport(position, rotation);
    }
    public void ResetVerticalVelocity()
    {
        vertSpeed = minFall;
        externalVelocity = dashVelocity = Vector3.zero;
        dashRemaining = 0;
        PlanarInputDirection = Vector3.zero;
        isJumping = false;
        jumpCooldownTimer = jumpSlowTimer = 0;
        pendingInputs.Clear();
        jumpQueued = false;
        correctionOffset = Vector3.zero;
        hasPreviousTick = false;
        if (isServer)
        {
            slowMultiplier = 1;
            slowUntil = 0;
            serverEpoch++;
            lastReceived = lastProcessed = 0;
            clientSequence = 0;
            serverInputs.Clear();
            lastServerInput = default;
        }
    }
    public void ForceGroundReset() => vertSpeed = minFall;
}