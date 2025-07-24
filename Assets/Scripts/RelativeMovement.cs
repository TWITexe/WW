using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Playables;

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

    [SerializeField] float jumpCooldown = 1f;
    private float jumpCooldownTimer = 0f;
    [SerializeField] float slowAfterJumpTime = 0.3f;
    [SerializeField] float speedMultiplier = 0.5f;
    private float jumpSlowTimer = 0.3f;
    private bool isJumping = false;

    private float vertSpeed;
    private CharacterController charController;
    private ControllerColliderHit contact;
    //private Animator animator;

    private void Start()
    {
        charController = GetComponent<CharacterController>();
        //animator = GetComponent<Animator>();
        vertSpeed = minFall;
    }
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        if (!isOwned || playerCamera == null)
            return;

        // паруслов чтоб не забыть
        // обновление таймеров прыжка и замедления после прыжка
        HandleTimers();

        // получение направления движения от игрока
        Vector3 movement = GetInputMovement();
        // применение ускорения/замедления (Shift, прыжок)
        movement = ApplySpeedModifiers(movement);
        // поворот игрока в направлении движения
        RotateTowardsMovement(movement);
        //animator.SetFloat("Speed", movement.sqrMagnitude);
        // где isRunning = true, если игрок бежит
        cameraShake.SetShaking(true, movement.sqrMagnitude);


        float distanceToGround;
        bool hitGround;
        // проверка на соприкосновение с землёй
        CheckGround(out hitGround, out distanceToGround);
        // обновление значения расстояния до земли
        //animator.SetFloat("DistanceToGround", distanceToGround);

        // обработка логики прыжка
        HandleJump(hitGround);
        // применение гравитации, если в воздухе
        ApplyGravity(hitGround, ref movement);

        // добавление вертикальной скорости к движению
        movement.y = vertSpeed;
        movement *= Time.deltaTime;
        // перемещение персонажа
        charController.Move(movement);
        
    }


    void HandleTimers()
    {
        if (jumpCooldownTimer > 0)
            jumpCooldownTimer -= Time.deltaTime;

        if (jumpSlowTimer > 0f)
            jumpSlowTimer -= Time.deltaTime;
    }

    Vector3 GetInputMovement()
    {
        float horInput = Input.GetAxis("Horizontal");
        float vertInput = Input.GetAxis("Vertical");

        Vector3 right = playerCamera.transform.right;
        Vector3 forward = Vector3.Cross(right, Vector3.up);

        return (right * horInput + forward * vertInput);
    }

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
        movement = Vector3.ClampMagnitude(movement, moveSpeed * currentSpeedMultiplier);

        return movement;
    }

    void RotateTowardsMovement(Vector3 movement)
    {
        if (movement != Vector3.zero)
        {
            Quaternion direction = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.Lerp(transform.rotation, direction, rotSpeed * Time.deltaTime);
        }
    }

    void CheckGround(out bool hitGround, out float distanceToGround)
    {
        distanceToGround = groundCheckDistance;
        hitGround = false;
        RaycastHit hit;

        if (vertSpeed < 0 && Physics.Raycast(transform.position, Vector3.down, out hit, groundCheckDistance))
        {
            distanceToGround = hit.distance;
            float check = (charController.height + charController.radius) / 1.9f;
            hitGround = hit.distance <= check;
        }
    }

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
                //animator.SetBool("Jumping", false);
            }
        }
        else
        {
            if (contact != null && !hitGround)
            {
                //animator.SetBool("Jumping", true);
                isJumping = true;
            }
        }
    }

    void ApplyGravity(bool hitGround, ref Vector3 movement)
    {
        if (!hitGround)
        {
            vertSpeed += gravity * 5 * Time.deltaTime;
            if (vertSpeed < terminalVelocity)
            {
                vertSpeed = terminalVelocity;
            }

            if (charController.isGrounded)
            {
                if (Vector3.Dot(movement, contact.normal) < 0)
                {
                    movement = contact.normal * moveSpeed;
                }
                else
                {
                    movement += contact.normal * moveSpeed;
                }
            }
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody rb = hit.collider.attachedRigidbody;

        if (rb != null && !rb.isKinematic)
        {
            // сила зависит от скорости игрока, можно кастомизировать
            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
            rb.AddForce(pushDir * 5f, ForceMode.Impulse);
        }
    }
}
