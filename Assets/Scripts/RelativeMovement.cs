using System.Collections.Generic;
using Mirror;
using UnityEngine;

// сервер и клиентское предсказание используют общий расчёт движения с фиксированным шагом.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(CharacterController))]
public partial class RelativeMovement : NetworkBehaviour
{
    [SerializeField] CameraShake cameraShake;
    [SerializeField] Camera playerCamera;
    [SerializeField] float rotSpeed = 8;
    [SerializeField] float moveSpeed = 9;
    [SerializeField, Min(1)] float sprintMultiplier = 1.5f;
    [SerializeField, Min(1)] float maxStamina = 100f;
    [SerializeField, Min(.1f)] float sprintDrainPerSecond = 20f;
    [SerializeField, Min(.1f)] float staminaRecoveryPerSecond = 25f;
    float stamina;
    bool sprinting;
    public float Stamina => stamina;
    public float MaxStamina => maxStamina;
    public bool IsSprinting => sprinting;
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
        public float vertical;
    }
    public struct MotorState
    {
        public uint epoch, tick, acknowledged;
        public Vector3 position, externalVelocity, dashVelocity;
        public Quaternion rotation;
        public float verticalSpeed, jumpCooldown, jumpSlow, dashRemaining, slowMultiplier;
        public float stamina;
        public bool sprinting;
        public double simulationTime;
        public double stunUntil;
        public Vector3 iceVelocity;
        public SpellControlRules.TimedSlow[] slows;
        public bool jumping, dead;
        public UltimateMotionState ultimateMotion;
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
    SpellControlRules.TimedSlow[] slows = System.Array.Empty<SpellControlRules.TimedSlow>();
    double nextPeriodicLiftAt;
    [SyncVar] double stunUntil;
    Vector3 iceVelocity;
    public bool IsStunned => NetworkTime.time < stunUntil;
    float jumpCooldownTimer, jumpSlowTimer, vertSpeed;
    bool isJumping;
    CharacterController controller;
    Health health;
    Vector3 externalVelocity, dashVelocity;
    float dashRemaining;
    Vector3 correctionOffset, previousTickPosition;
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
        stamina = maxStamina;
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
        sampledInput = ReadMovementInput();
        bool blocked = sampledInput.blocked;
        if (blocked) jumpQueued = false;
        else jumpQueued |= Input.GetButtonDown("Jump");
        PlanarInputDirection = IsUltimateRooted ? Vector3.zero : sampledInput.movement;
        cameraShake?.SetShaking(!blocked && ProbeGround() && PlanarInputDirection.sqrMagnitude > .1f,
            moveSpeed * moveSpeed * (sprinting ? sprintMultiplier * sprintMultiplier : 1) * slowMultiplier * slowMultiplier);
    }
    // читаем несглаженные оси: отпускание клавиши сразу даёт нулевое направление.
    MoveInput ReadMovementInput()
    {
        bool blocked = PlayerGameUI.InputBlocked || IsStunned || (health != null && health.IsDead);
        Vector3 forward = playerCamera != null
            ? Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized : transform.forward;
        Vector3 right = playerCamera != null
            ? Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized : transform.right;
        return new MoveInput
        {
            forward = forward,
            movement = blocked ? Vector3.zero : (right * Input.GetAxisRaw("Horizontal") + forward * Input.GetAxisRaw("Vertical")).normalized,
            sprint = !blocked && Input.GetKey(KeyCode.LeftShift),
            vertical = blocked ? 0 : (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1 : 0),
            blocked = blocked
        };
    }
    void FixedUpdate()
    {
        if (isLocalPlayer) { previousTickPosition = transform.position; hasPreviousTick = true; }
        if (isLocalPlayer && (isServer || clientEpoch != 0))
        {
            // обновляем ввод перед физическим шагом, не дожидаясь следующего Update.
            MoveInput input = ReadMovementInput();
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
            Mathf.Abs(input.movement.y) > .001f || Mathf.Abs(input.forward.y) > .001f ||
            float.IsNaN(input.vertical) || float.IsInfinity(input.vertical) || Mathf.Abs(input.vertical) > 1)
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
            // используем последнее состояние кнопок без доигрывания устаревшей ходьбы.
            // короткий запрос прыжка сохраняем, даже если следом пришло отпускание.
            while (serverInputs.Count > 0)
            {
                MoveInput latest = serverInputs.Dequeue();
                latest.jump |= input.jump;
                input = latest;
            }
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
        if (!NetManager.CombatAllowed || !controller.enabled || (health != null && health.IsDead)) { sprinting = false; return; }
        if (SimulateUltimate(ref input, dt, simulationTime)) return;
        bool stunned = simulationTime < stunUntil;
        if (stunned) { input.blocked = true; input.jump = false; dashRemaining = 0; iceVelocity = Vector3.zero; }
        slowMultiplier = SpellControlRules.SlowMultiplier(slows, simulationTime);
        if (slowMultiplier == 1 && slows.Length > 0)
            slows = System.Array.Empty<SpellControlRules.TimedSlow>();
        jumpCooldownTimer = Mathf.Max(0, jumpCooldownTimer - dt);
        jumpSlowTimer = Mathf.Max(0, jumpSlowTimer - dt);
        bool grounded = vertSpeed <= 0 && ProbeGround();
        if (slamDamage > 0 && isServer) CheckUltimateLanding(grounded, simulationTime);
        bool jumped = false;
        if (grounded)
        {
            if (isJumping) { isJumping = false; jumpSlowTimer = slowAfterJumpTime; }
            vertSpeed = minFall;
            if (!input.blocked && input.jump && jumpCooldownTimer <= 0)
            {
                vertSpeed = simulationTime < ultimateMotion.spiritUntil ? jumpSpeed * 1.4f : jumpSpeed;
                jumpCooldownTimer = jumpCooldown;
                isJumping = true;
                jumped = true;
            }
        }
        if (!grounded && !jumped && simulationTime < ultimateMotion.gravityUntil)
            vertSpeed = Mathf.Clamp((ultimateMotion.hoverY - transform.position.y) * 2, -4, 5);
        else if (!grounded && !jumped)
            vertSpeed = Mathf.Max(terminalVelocity, vertSpeed + gravity * (vertSpeed < 0 ? fallGravityMultiplier : 5) * dt);
        if (grounded && simulationTime < ultimateMotion.gravityUntil) { vertSpeed = 5; grounded = false; }
        Vector3 movement = input.blocked ? Vector3.zero : Vector3.ClampMagnitude(input.movement, 1);
        float multiplier = slowMultiplier;
        if (simulationTime < ultimateMotion.spiritUntil) multiplier *= .65f;
        // сервер и предсказание одинаково ограничивают ускорение доступным запасом стамины.
        bool wantsSprint = !input.blocked && input.sprint && movement.sqrMagnitude > .001f;
        float sprintFraction = wantsSprint ? Mathf.Clamp01(stamina / Mathf.Max(.0001f, sprintDrainPerSecond * dt)) : 0;
        sprinting = sprintFraction > 0;
        multiplier *= Mathf.Lerp(1, sprintMultiplier, sprintFraction);
        Vector3 positionBeforeMove = transform.position;
        if (isJumping) multiplier *= speedMultiplier;
        if (jumpSlowTimer > 0) multiplier *= speedMultiplier;
        movement *= moveSpeed * multiplier;
        // плавный разгон и торможение действуют на льду одинаково на сервере и клиенте.
        if (grounded && !stunned && OnIceBridge())
        {
            iceVelocity = Vector3.Lerp(iceVelocity, movement, 1 - Mathf.Exp(-2.5f * dt));
            movement = iceVelocity;
        }
        else iceVelocity = Vector3.ProjectOnPlane(movement, Vector3.up);
        if (!input.blocked && input.forward.sqrMagnitude > .01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(input.forward), 1 - Mathf.Exp(-rotSpeed * dt));
        movement.y = vertSpeed;
        externalVelocity = Vector3.Lerp(externalVelocity, Vector3.zero, 5 * dt);
        float dashStep = Mathf.Min(dt, dashRemaining);
        dashRemaining = Mathf.Max(0, dashRemaining - dt);
        controller.Move((movement + externalVelocity) * dt + dashVelocity * dashStep);
        Vector3 displacement = Vector3.ProjectOnPlane(transform.position - positionBeforeMove, Vector3.up);
        // упор в стену не расходует запас; ходьба, прыжок, рывок и отталкивание не восстанавливают его.
        bool moved = displacement.sqrMagnitude > .000001f;
        if (sprinting && moved) stamina = Mathf.Max(0, stamina - sprintDrainPerSecond * dt * sprintFraction);
        else if (grounded && !jumped && input.movement.sqrMagnitude < .001f && !moved &&
            dashStep <= 0 && externalVelocity.sqrMagnitude < .01f)
            stamina = Mathf.Min(maxStamina, stamina + staminaRecoveryPerSecond * dt);
        sprinting &= moved;
    }

    MotorState CaptureState() => new MotorState
    {
        epoch = serverEpoch, tick = serverTick, acknowledged = lastProcessed,
        position = transform.position, rotation = transform.rotation,
        verticalSpeed = vertSpeed, externalVelocity = externalVelocity,
        dashVelocity = dashVelocity, dashRemaining = dashRemaining,
        jumpCooldown = jumpCooldownTimer, jumpSlow = jumpSlowTimer, jumping = isJumping,
        slowMultiplier = slowMultiplier, slows = slows, simulationTime = NetworkTime.time,
        stamina = stamina, sprinting = sprinting,
        stunUntil = stunUntil, iceVelocity = iceVelocity,
        dead = health != null && health.IsDead,
        ultimateMotion = ultimateMotion
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
        slows = state.slows ?? System.Array.Empty<SpellControlRules.TimedSlow>();
        stamina = Mathf.Clamp(state.stamina, 0, maxStamina);
        sprinting = state.sprinting;
        stunUntil = state.stunUntil;
        iceVelocity = state.iceVelocity;
        ultimateMotion = state.ultimateMotion;
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
    // оглушение запрещает ввод и рывок, но сохраняет гравитацию и внешнее отталкивание.
    [Server] public void ServerStun(float duration)
    {
        if (health != null && health.IsDead) return;
        stunUntil = System.Math.Max(stunUntil, NetworkTime.time + Mathf.Clamp(duration, 0, 5));
        dashRemaining = 0;
    }

    private bool OnIceBridge()
    {
        Vector3 feet = transform.TransformPoint(controller.center) - Vector3.up * controller.height * transform.lossyScale.y * .5f;
        return Physics.Raycast(feet + Vector3.up * .15f, Vector3.down, out var hit, .35f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider.GetComponent<IceBridgeSurface>() != null;
    }
    // горизонтальное движение области сохраняется; повторный подброс имеет общий срок для всех областей.
    [Server] public void ServerAddPeriodicForce(Vector3 force)
    {
        if (!Finite(force) || (health != null && health.IsDead)) return;
        AddExternalForce(SpellControlRules.LimitPeriodicLift(force, NetworkTime.time, ref nextPeriodicLiftAt));
    }
    [Server] public void ServerDash(Vector3 direction)
    {
        if (!Finite(direction) || IsStunned || (health != null && health.IsDead)) return;
        dashVelocity = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * 24;
        dashRemaining = .22f;
    }
    [Server] public void ApplySlow(float multiplier, float duration)
    {
        if (health != null && health.IsDead) return;
        slows = SpellControlRules.AddSlow(slows, multiplier, duration, NetworkTime.time);
        slowMultiplier = SpellControlRules.SlowMultiplier(slows, NetworkTime.time);
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
            ultimateMotion = default;
            slamDamage = 0;
            slowMultiplier = 1;
            slows = System.Array.Empty<SpellControlRules.TimedSlow>();
            nextPeriodicLiftAt = 0;
            stunUntil = 0;
            iceVelocity = Vector3.zero;
            stamina = maxStamina;
            sprinting = false;
            serverEpoch++;
            lastReceived = lastProcessed = 0;
            clientSequence = 0;
            serverInputs.Clear();
            lastServerInput = default;
        }
    }
    public void ForceGroundReset() => vertSpeed = minFall;
}
