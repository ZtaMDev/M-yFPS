using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float jumpForce = 6f;
    public float slideSpeed = 10f;
    public float slideDuration = 0.5f;
    public float recoveryDuration = 0.1f;

    private Rigidbody rb;
    private bool isGrounded;
    private bool isSliding = false;
    private bool isRecovering = false;
    private bool isCrouching = false;
    private float slideTimer = 0f;
    private float recoveryTimer = 0f;
    private bool cPressedDuringSlide = false;

    private Vector3 slideDirection;
    private Vector3 momentum;

    public Transform cameraHolder;
    private Vector3 originalCameraPos;
    public Vector3 slideCameraOffset = new Vector3(0f, -0.5f, 0f);
    public float cameraTransitionSpeed = 6f;
    private bool justCancelledWithSpace = false;
    private Vector3 airMomentum = Vector3.zero;
    private bool awaitingAirDirection = false;
    private bool airDirectionLocked = false;
    public float airControlMultiplierFromIdle = 0.2f;
    private CapsuleCollider capsule;
    private float originalHeight;
    private Vector3 originalCenter;
    public float crouchHeight = 1f;
    public Vector3 crouchCenterOffset = new Vector3(0f, -0.5f, 0f);
    public float slideCooldown = 0.3f;
    private float slideCooldownTimer = 0f;
    public float crouchSpeedMultiplier = 0.5f; // ajusta entre 0.1 y 1.0


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        originalHeight = capsule.height;
        originalCenter = capsule.center;
        originalCameraPos = cameraHolder.localPosition;
    }

    void Update()
    {
        HandleSlide();
        HandleCrouch();
        Jump();
        TransitionCamera();

        if (awaitingAirDirection && !isGrounded && !airDirectionLocked)
        {
            float moveX = Input.GetAxisRaw("Horizontal");
            float moveZ = Input.GetAxisRaw("Vertical");
            Vector3 inputDirection = (transform.right * moveX + transform.forward * moveZ).normalized;

            if (inputDirection != Vector3.zero)
            {
                airMomentum = inputDirection * moveSpeed;
                airDirectionLocked = true;
                awaitingAirDirection = false;
            }
        }
        
        if (slideCooldownTimer > 0f)
        {
            slideCooldownTimer -= Time.deltaTime;
        }

    }

    void FixedUpdate()
    {
        Move();
    }

    void Move()
    {
        if (isSliding) return;

        Vector3 velocity;

        if (isGrounded)
        {
            float moveX = Input.GetAxis("Horizontal");
            float moveZ = Input.GetAxis("Vertical");
            Vector3 moveInput = transform.right * moveX + transform.forward * moveZ;

            float currentSpeed = moveSpeed;
            if (isRecovering) currentSpeed *= crouchSpeedMultiplier;
            if (isCrouching) currentSpeed *= crouchSpeedMultiplier;

            velocity = new Vector3(moveInput.x * currentSpeed, rb.velocity.y, moveInput.z * currentSpeed);
            momentum = moveInput.normalized * currentSpeed;
        }
        else
        {
            Vector3 airMoveInput = Vector3.zero;
            if (airMomentum == Vector3.zero)
            {
                float moveX = Input.GetAxis("Horizontal");
                float moveZ = Input.GetAxis("Vertical");
                airMoveInput = (transform.right * moveX + transform.forward * moveZ).normalized * moveSpeed * airControlMultiplierFromIdle;
            }

            Vector3 combinedAir = airMomentum + airMoveInput;
            velocity = new Vector3(combinedAir.x, rb.velocity.y, combinedAir.z);
        }

        rb.velocity = velocity;
    }

    void Jump()
    {
        if (justCancelledWithSpace)
        {
            justCancelledWithSpace = false;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space) && isGrounded && !isSliding)
        {
            if (isCrouching)
            {
                CancelCrouch();
                return;
            }

            float moveX = Input.GetAxis("Horizontal");
            float moveZ = Input.GetAxis("Vertical");
            Vector3 directionalInput = (transform.right * moveX + transform.forward * moveZ).normalized;

            if (directionalInput == Vector3.zero)
            {
                awaitingAirDirection = true;
                airDirectionLocked = false;
                airMomentum = Vector3.zero;
            }
            else
            {
                airMomentum = directionalInput * moveSpeed;
                airDirectionLocked = true;
                awaitingAirDirection = false;
            }

            Vector3 jumpVelocity = airMomentum + Vector3.up * jumpForce;
            rb.velocity = jumpVelocity;
        }
    }

    void HandleSlide()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (!isSliding && isGrounded)
            {
                bool isMoving = Mathf.Abs(Input.GetAxis("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxis("Vertical")) > 0.1f;

                if (isMoving && !isCrouching)
                {
                    StartSlide();
                }
            }
            else if (isSliding)
            {
                cPressedDuringSlide = true;
            }
            else if (isCrouching)
            {
                CancelCrouch();
            }
        }

        if (isSliding && Input.GetKeyDown(KeyCode.Space))
        {
            cPressedDuringSlide = true;
            justCancelledWithSpace = true;
        }

        if (isSliding)
        {
            slideTimer -= Time.deltaTime;

            // 💡 Nuevo: chequeo por velocidad baja antes de que termine slideTimer
            float horizontalSpeed = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            if (horizontalSpeed <= 1f && slideTimer > 0f && !cPressedDuringSlide)
            {
                EndSlide(false); // activa crouch anticipado si no se canceló
                return;
            }

            if (slideTimer <= 0f || cPressedDuringSlide)
            {
                EndSlide(cPressedDuringSlide);
            }
        }


        if (isRecovering)
        {
            recoveryTimer -= Time.deltaTime;
            if (recoveryTimer <= 0f) isRecovering = false;
        }
    }

    void StartSlide()
    {
        if (slideCooldownTimer > 0f) return; // bloquea slide si está en cooldown
        isSliding = true;
        slideTimer = slideDuration;
        slideCooldownTimer = slideCooldown; // arranca cooldown
        cPressedDuringSlide = false;

        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");
        Vector3 moveInput = transform.right * moveX + transform.forward * moveZ;
        slideDirection = moveInput.normalized;
        if (slideDirection == Vector3.zero) slideDirection = transform.forward;

        rb.velocity = slideDirection * slideSpeed;
        capsule.height = crouchHeight;
        capsule.center = originalCenter + crouchCenterOffset;
        isCrouching = false;
    }

    void EndSlide(bool cancelled)
    {
        isSliding = false;
        isRecovering = true;
        recoveryTimer = recoveryDuration;
        cPressedDuringSlide = false;

        if (cancelled)
        {
            rb.velocity = momentum;
            CancelCrouch();
        }
        else
        {
            // Primero activamos crouch, luego reducimos la velocidad
            isCrouching = true;
            capsule.height = crouchHeight;
            capsule.center = originalCenter + crouchCenterOffset;

            rb.velocity = slideDirection * (slideSpeed * 0.5f);
        }
    }


    void HandleCrouch()
    {
        if (!isGrounded) return; // no hacer nada si estás en el aire

        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            if (isSliding) return; // no permitir crouch mientras estás deslizando

            if (isCrouching)
            {
                CancelCrouch();
            }
            else
            {
                capsule.height = crouchHeight;
                capsule.center = originalCenter + crouchCenterOffset;
                isCrouching = true;
            }
        }
    }

    void CancelCrouch()
    {
        capsule.height = originalHeight;
        capsule.center = originalCenter;
        isCrouching = false;
    }

    void TransitionCamera()
    {
        bool lowered = isSliding || isCrouching;
        Vector3 targetPos = lowered ? originalCameraPos + slideCameraOffset : originalCameraPos;
        cameraHolder.localPosition = Vector3.Lerp(cameraHolder.localPosition, targetPos, Time.deltaTime * cameraTransitionSpeed);
    }

    void OnCollisionStay(Collision collision)
    {
        isGrounded = true;
    }

    void OnCollisionExit(Collision collision)
    {
        isGrounded = false;
    }
}
