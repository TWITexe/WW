using Mirror;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class RelativeMovement : NetworkBehaviour
{
    [SerializeField] CameraShake cameraShake;
    [SerializeField] Camera playerCamera;

    [SerializeField] float rotSpeed = 15.0f;
    [SerializeField] float moveSpeed = 6.0f;
    [SerializeField] float jumpSpeed = 15.0f;
    [SerializeField] float gravity = -9.8f;
    [SerializeField] float terminalVelocity = -10.0f;
    [SerializeField] float minFall = -1.5f;
    [SerializeField] float groundCheckDistance = 10f;
    [SerializeField] float slowAfterJumpTime = 0.3f;
    [SerializeField] float speedMultiplier = 0.5f;
    [SerializeField] float jumpCooldown = 1f;

    private float jumpCooldownTimer = 0f;
    private float jumpSlowTimer = 0.3f;
    private bool isJumping = false;

    private float vertSpeed;
    private CharacterController charController;
    private ControllerColliderHit contact;
    private Health health;

    // ВНЕШНЯЯ СИЛА (WindFlow, knockback и т.д.)
    private Vector3 externalVelocity;

    // ===================== STATE =====================
    // локальная блокировка ввода (чтобы не зависеть от сети)
    private bool inputLocked;

    private void Start()
    {
        charController = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        vertSpeed = minFall;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        if (playerCamera != null)
            playerCamera.gameObject.SetActive(true);
    }

    void Update()
    {
        // ===================== INPUT LOCK =====================
        if (!isOwned || playerCamera == null || (health != null && health.IsDead))
            return;

        HandleTimers();

        Vector3 movement = GetInputMovement();

        AlignPlayerToCamera();

        movement = ApplySpeedModifiers(movement);

        CheckGround(out bool hitGround, out float distanceToGround);

        HandleJump(hitGround);

        ApplyGravity(hitGround, ref movement);

        movement.y = vertSpeed;

        externalVelocity = Vector3.Lerp(externalVelocity, Vector3.zero, 5f * Time.deltaTime);
        movement += externalVelocity;

        charController.Move(movement * Time.deltaTime);

        cameraShake?.SetShaking(true, movement.sqrMagnitude);
    }

    // ===================== TIMERS =====================
    void HandleTimers()
    {
        if (jumpCooldownTimer > 0)
            jumpCooldownTimer -= Time.deltaTime;

        if (jumpSlowTimer > 0f)
            jumpSlowTimer -= Time.deltaTime;
    }

    // ===================== INPUT =====================
    Vector3 GetInputMovement()
    {
        float horInput = Input.GetAxis("Horizontal");
        float vertInput = Input.GetAxis("Vertical");

        Vector3 right = playerCamera.transform.right;
        right.y = 0;

        Vector3 forward = playerCamera.transform.forward;
        forward.y = 0;

        return (right * horInput + forward * vertInput).normalized;
    }

    void AlignPlayerToCamera()
    {
        Vector3 forward = playerCamera.transform.forward;
        forward.y = 0;

        if (forward.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(forward);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, rotSpeed * Time.deltaTime);
        }
    }

    // ===================== SPEED =====================
    Vector3 ApplySpeedModifiers(Vector3 movement)
    {
        float currentSpeedMultiplier = 1f;

        if (Input.GetKey(KeyCode.LeftShift))
            currentSpeedMultiplier *= 1.5f;

        if (isJumping)
            currentSpeedMultiplier *= speedMultiplier;

        if (jumpSlowTimer > 0f)
            currentSpeedMultiplier *= speedMultiplier;

        movement *= moveSpeed * currentSpeedMultiplier;

        return Vector3.ClampMagnitude(movement, moveSpeed * currentSpeedMultiplier);
    }

    // ===================== GROUND CHECK =====================
    void CheckGround(out bool hitGround, out float distanceToGround)
    {
        distanceToGround = groundCheckDistance;
        hitGround = false;

        RaycastHit hit;

        if (vertSpeed < 0 &&
            Physics.Raycast(transform.position, Vector3.down, out hit, groundCheckDistance))
        {
            distanceToGround = hit.distance;

            float check = (charController.height + charController.radius) / 1.9f;
            hitGround = hit.distance <= check;
        }
    }

    // ===================== JUMP =====================
    void HandleJump(bool hitGround)
    {
        if (hitGround)
        {
            if (Input.GetButtonDown("Jump") && jumpCooldownTimer <= 0)
            {
                vertSpeed = jumpSpeed;
                isJumping = true;
                jumpCooldownTimer = jumpCooldown;
            }
            else
            {
                if (isJumping)
                {
                    jumpSlowTimer = slowAfterJumpTime;
                    isJumping = false;
                }

                vertSpeed = minFall;
            }
        }
    }

    // ===================== GRAVITY =====================
    void ApplyGravity(bool hitGround, ref Vector3 movement)
    {
        if (!hitGround)
        {
            vertSpeed += gravity * 5f * Time.deltaTime;

            if (vertSpeed < terminalVelocity)
                vertSpeed = terminalVelocity;
        }
        else if (vertSpeed < 0)
        {
            vertSpeed = minFall;
        }
    }

    // ===================== EXTERNAL FORCE =====================
    // используется WindFlow, отбрасывания и т.д.
    public void AddExternalForce(Vector3 force)
    {
        externalVelocity += force;
    }

    // вызывается только сервером, когда кто-то должен получить отталкивание
    [Server]
    public void ServerAddExternalForce(Vector3 force)
    {
        // отправляем отталкивание именно клиенту-владельцу этого игрока
        TargetAddExternalForce(connectionToClient, force);
    }

    // выполняется только на клиенте-владельце игрока
    [TargetRpc]
    private void TargetAddExternalForce(NetworkConnectionToClient target, Vector3 force)
    {
        AddExternalForce(force);
    }

    // ===================== COLLISIONS =====================
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        contact = hit;

        Rigidbody rb = hit.collider.attachedRigidbody;

        if (rb != null && !rb.isKinematic)
        {
            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
            rb.AddForce(pushDir * 5f, ForceMode.Impulse);
        }
    }

    // ===================== DEATH CONTROL =====================

    // вызывается сервером через Health (SyncVar hook)
    private void OnDeadChanged(bool oldValue, bool newValue)
    {

        if (newValue)
        {
            inputLocked = true;
        }
        else
        {
            StartCoroutine(UnlockAfterRespawn());
        }
    }

    private IEnumerator UnlockAfterRespawn()
    {
        // ждём 1 кадр, чтобы Mirror синхронизировал состояние
        yield return null;

        vertSpeed = minFall;
        externalVelocity = Vector3.zero;
        isJumping = false;

        inputLocked = false;
    }

    // ===================== RESET API =====================
    // вызывается сервером при respawn (если нужно вручную)
    public void ResetVerticalVelocity()
    {
        vertSpeed = minFall;
    }

    public void ForceGroundReset()
    {
        vertSpeed = -2f;
    }
}