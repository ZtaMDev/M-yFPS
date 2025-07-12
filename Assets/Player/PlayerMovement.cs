using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMovement : MonoBehaviour
{
   [Header("Movement")]
    [Tooltip("Velocidad normal al caminar.")]
    public float moveSpeed = 5f;

    [Tooltip("Velocidad durante el sprint táctico.")]
    public float sprintTacticalSpeed = 8f;

    [Tooltip("Fuerza del salto vertical.")]
    public float jumpForce = 6f;

    [Header("Sprint Táctico")]
    [Tooltip("Duración máxima del sprint táctico en segundos.")]
    public float sprintMaxDuration = 3f;

    [Tooltip("Velocidad de recarga de energía para el sprint táctico.")]
    public float sprintRechargeRate = 1.5f;

    [Tooltip("Si está activo, el sprint táctico se activa automáticamente cuando hay energía.")]
    public bool sprintAutoEnabled = false;

    [Tooltip("Tiempo actual disponible para el sprint táctico.")]
    public float sprintTimer = 0f;

    // Estado interno (oculto)
    private bool sprintDrained = false;
    private bool isSprintingTactical = false;

    [Header("Slide")]
    [Tooltip("Velocidad durante el deslizamiento.")]
    public float slideSpeed = 10f;

    [Tooltip("Duración máxima del slide en segundos.")]
    public float slideDuration = 0.5f;

    [Tooltip("Tiempo de enfriamiento entre slides.")]
    public float slideCooldown = 0.3f;

    // Estado interno
    private float slideTimer, slideCooldownTimer;

    [Header("Dropshot")]
    [Tooltip("Impulso vertical inicial al hacer dropshot.")]
    public float dropshotUpwardImpulse = 4f;

    [Tooltip("Multiplicador de velocidad hacia adelante durante dropshot.")]
    public float dropshotForwardMultiplier = 0.85f;

    [Header("Crouch")]
    [Tooltip("Altura del collider en posición de crouch.")]
    public float crouchHeight = 1f;

    [Tooltip("Ajuste de centro del collider al agacharse.")]
    public Vector3 crouchCenterOffset = new Vector3(0f, -0.5f, 0f);

    [Header("Camera")]
    [Tooltip("Referencia al transform del holder de cámara.")]
    public Transform cameraHolder;

    [Tooltip("Offset de cámara durante slide.")]
    public Vector3 slideCameraOffset = new Vector3(0f, -0.5f, 0f);

    [Tooltip("Offset de cámara durante dropshot.")]
    public Vector3 dropshotCameraOffset = new Vector3(0f, -1.5f, -0.3f);

    [Tooltip("Velocidad de transición de la cámara.")]
    public float cameraTransitionSpeed = 6f;

    [Header("Air Control")]
    [Tooltip("Control de movimiento en aire si el jugador no tiene impulso.")]
    public float airControlMultiplierFromIdle = 0.2f;

    // Internos
    private bool justCancelledAction = false;
    private bool awaitingAirDirection = false, airDirectionLocked = false;
    private Vector3 airMomentum = Vector3.zero;

    private enum State { Normal, Crouch, Slide, Dropshot }
    private State currentState = State.Normal;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private float originalHeight;
    private Vector3 originalCenter, originalCameraPos;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        sprintTimer = sprintMaxDuration; // inicia lleno

        originalHeight = capsule.height;
        originalCenter = capsule.center;
        originalCameraPos = cameraHolder.localPosition;
    }

    void Update()
    {
        // 1) Tiempo de cooldown para nuevos slides
        if (slideCooldownTimer > 0f) slideCooldownTimer -= Time.deltaTime;
        
        // 2) Entradas
        HandleSlideInput();
        HandleCrouchAndDropshotInput();
        HandleJumpInput();
        TransitionCamera();
        HandleSprintTactical();
        // 3) Aire (post-jump)
        if (awaitingAirDirection && !IsGrounded() && !airDirectionLocked)
        {
            float mx = Input.GetAxisRaw("Horizontal");
            float mz = Input.GetAxisRaw("Vertical");
            Vector3 dir = (transform.right * mx + transform.forward * mz).normalized;
            if (dir != Vector3.zero)
            {
                airMomentum = dir * moveSpeed;
                airDirectionLocked = true;
                awaitingAirDirection = false;
            }
        }
    }

    void FixedUpdate()
    {
        ApplyMovement();
    }

    // ————————————————————————————————————————————————————————————————————————
    // 1) MOVE & AIR CONTROL
    void ApplyMovement()
    {
        // no sobreescribir durante slide o dropshot
        if (currentState == State.Slide || currentState == State.Dropshot) return;

        Vector3 vel = rb.velocity;

        if (IsGrounded())
        {
            float mx = Input.GetAxis("Horizontal");
            float mz = Input.GetAxis("Vertical");
            Vector3 inputDir = (transform.right * mx + transform.forward * mz).normalized;

            float speed = moveSpeed;

            if (currentState == State.Crouch) speed *= 0.5f;
            else if (isSprintingTactical && currentState == State.Normal)
                speed = sprintTacticalSpeed;

            vel.x = inputDir.x * speed;
            vel.z = inputDir.z * speed;
            // guardamos momentum para cancelar slide
            airMomentum = inputDir * speed;
        }
        else
        {
            Vector3 correction = Vector3.zero;
            if (airDirectionLocked == false)
            {
                float mx = Input.GetAxis("Horizontal");
                float mz = Input.GetAxis("Vertical");
                Vector3 inAirDir = (transform.right * mx + transform.forward * mz).normalized;
                correction = inAirDir * moveSpeed * airControlMultiplierFromIdle;
            }
            Vector3 combined = airMomentum + correction;
            vel.x = combined.x;
            vel.z = combined.z;
        }

        rb.velocity = vel;
    }
    void HandleSprintTactical()
    {
        float mx = Input.GetAxisRaw("Horizontal");
        float mz = Input.GetAxisRaw("Vertical");
        bool isMoving = new Vector3(mx, 0f, mz).magnitude > 0.1f;
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift);
        bool cancelledSlide = Input.GetKeyDown(KeyCode.Space) && currentState == State.Slide;

        // ¿Está pidiendo sprint?
        bool sprintRequest = (shiftHeld || sprintAutoEnabled) 
                            && currentState == State.Normal 
                            && isMoving;

        // 1) Si estamos drenados, recarga hasta full y reactiva sólo si se sigue pidiendo
        if (sprintDrained)
        {
            sprintTimer += Time.deltaTime * sprintRechargeRate;
            if (sprintTimer >= sprintMaxDuration)
            {
                sprintTimer = sprintMaxDuration;
                sprintDrained = false;
                if (sprintRequest) 
                    isSprintingTactical = true;
            }
            return;
        }

        // 2) Si estamos esprintando, gastar o detener
        if (isSprintingTactical)
        {
            if (!sprintRequest)
            {
                // soltó Shift / no quiere auto sprint → detenemos
                isSprintingTactical = false;
            }
            else
            {
                // sigue esprintando → drenamos
                sprintTimer -= Time.deltaTime;
                if (sprintTimer <= 0f)
                {
                    sprintTimer = 0f;
                    isSprintingTactical = false;
                    sprintDrained = true;  // marcamos drenado
                }
            }
            return;
        }

        // 3) Si no estamos esprintando y hay energía + petición → arrancamos sprint
        if (sprintRequest && sprintTimer > 0f)
        {
            isSprintingTactical = true;
            return;
        }

        // 4) Recarga incidental (quieto, caminar, slide, dropshot, cancel slide)
        if (!isMoving 
            || cancelledSlide 
            || currentState == State.Slide 
            || currentState == State.Dropshot)
        {
            sprintTimer += Time.deltaTime * sprintRechargeRate;
            if (sprintTimer > sprintMaxDuration)
                sprintTimer = sprintMaxDuration;
        }
    }
    // ————————————————————————————————————————————————————————————————————————
    // 2) SLIDE
    void HandleSlideInput()
    {
        if (currentState == State.Dropshot && !IsGrounded())
        {
            return;
        }

        // C para slide / cancel slide / cancel crouch
        if (Input.GetKeyDown(KeyCode.C) && IsGrounded())
        {
            // START SLIDE
            if (currentState == State.Normal && slideCooldownTimer <= 0f && isSprintingTactical)
            {
                float moveX = Input.GetAxis("Horizontal"), moveZ = Input.GetAxis("Vertical");
                Vector3 dir = (transform.right * moveX + transform.forward * moveZ).normalized;
                if (dir.magnitude > 0.1f)
                    StartSlide(dir);
            }
            // CANCEL SLIDE PREMATURELY
            else if (currentState == State.Slide)
            {
                CancelSlide();
            }
            // CANCEL CROUCH
            else if (currentState == State.Crouch)
            {
                CancelCrouch();
            }
        }

        // SPACE cancela slide (1ª) y bloquea salto, luego 2ª SPACE salta
        if (currentState == State.Slide && Input.GetKeyDown(KeyCode.Space))
        {
            CancelSlide();
            justCancelledAction = true;
        }

        // timer interno de slide
        if (currentState == State.Slide)
        {
            slideTimer -= Time.deltaTime;

            // 🚦 Detectar si la velocidad ya es baja y terminar slide automáticamente
            float horizontalSpeed = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            if (horizontalSpeed <= 1f || slideTimer <= 0f)
            {
                EndSlide();
            }
        }

    }

    void StartSlide(Vector3 dir)
    {
        currentState = State.Slide;
        slideTimer = slideDuration;
        slideCooldownTimer = slideCooldown;

        capsule.height = crouchHeight;
        capsule.center = originalCenter + crouchCenterOffset;

        rb.velocity = dir * slideSpeed;
    }

    void CancelSlide()
    {
        currentState = State.Normal;
        capsule.height = originalHeight;
        capsule.center = originalCenter;
        // devolvemos momentum de movimiento
        rb.velocity = airMomentum;
    }

    void EndSlide()
    {
        currentState = State.Normal;
        capsule.height = originalHeight;
        capsule.center = originalCenter;

        Vector3 horizontalDir = new Vector3(rb.velocity.x, 0f, rb.velocity.z).normalized;
        rb.velocity = horizontalDir * moveSpeed; // ↔ retomar velocidad normal suavemente
    }



    // ————————————————————————————————————————————————————————————————————————
    // 3) CROUCH Y DROPSHOT
    void HandleCrouchAndDropshotInput()
    {
        if (!IsGrounded()) return;

        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            float mx = Input.GetAxis("Horizontal"), mz = Input.GetAxis("Vertical");
            Vector3 dir = (transform.right * mx + transform.forward * mz).normalized;
            bool moving = dir.magnitude > 0.1f;

            // si está en Slide, no hacemos nada
            if (currentState == State.Slide) return;

            // START DROPSHOT
            if (moving && currentState == State.Normal && isSprintingTactical)
            {
                StartDropshot(dir);
            }
            // TOGGLE CROUCH
            else if (currentState == State.Crouch)
            {
                CancelCrouch();
            }
            else if (currentState == State.Normal)
            {
                EnterCrouch();
            }
        }

        // SPACE cancela dropshot (1ª) → vuelve a Normal, luego 2ª SPACE salta
        if (currentState == State.Dropshot && Input.GetKeyDown(KeyCode.Space))
        {
            if (IsGrounded()) // 👈 solo permite cancelar cuando está en el piso
            {
                CancelDropshot();
                justCancelledAction = true;
            }
        }

    }

    void EnterCrouch()
    {
        currentState = State.Crouch;
        capsule.height = crouchHeight;
        capsule.center = originalCenter + crouchCenterOffset;
        rb.velocity = Vector3.zero;
    }

    void CancelCrouch()
    {
        currentState = State.Normal;
        capsule.height = originalHeight;
        capsule.center = originalCenter;
    }

    void StartDropshot(Vector3 dir)
    {
        currentState = State.Dropshot;

        capsule.height = crouchHeight;
        capsule.center = originalCenter + crouchCenterOffset;

        // impulso: adelante + un boost hacia arriba
        Vector3 up = Vector3.up * dropshotUpwardImpulse;
        Vector3 forward = dir * (moveSpeed * dropshotForwardMultiplier);
        rb.velocity = forward + up;
    }

    void CancelDropshot()
    {
        currentState = State.Normal;
        capsule.height = originalHeight;
        capsule.center = originalCenter;
    }

    // ————————————————————————————————————————————————————————————————————————
    // 4) SALTO (UNIFICADO SLIDE + DROPSHOT CANCEL)
    void HandleJumpInput()
    {
         // ── 0) Bloquear salto/cancel mientras estés en Dropshot y AÚN en el aire
        if (currentState == State.Dropshot && !IsGrounded())
            return;
        // espacio: cancelar estado si estamos en Slide/Crouch/Dropshot
        // y bloquear salto UNA SOLA VEZ
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // 1ª prioridad: Cancelar Slide
            if (currentState == State.Slide)
            {
                CancelSlide();
                justCancelledAction = true;
                return;
            }

            // 2ª prioridad: Cancelar Dropshot
            if (currentState == State.Dropshot)
            {
                CancelDropshot();
                justCancelledAction = true;
                return;
            }

            // 3ª prioridad: Cancelar Crouch
            if (currentState == State.Crouch)
            {
                CancelCrouch();
                justCancelledAction = true;
                return;
            }

            // 4ª: Si estamos en Normal y en piso, saltar (respetando cancel)
            if (IsGrounded() && !justCancelledAction)
            {
                float mx = Input.GetAxis("Horizontal");
                float mz = Input.GetAxis("Vertical");
                Vector3 dir = (transform.right * mx + transform.forward * mz).normalized;

                if (dir == Vector3.zero)
                {
                    awaitingAirDirection = true;
                    airDirectionLocked = false;
                    airMomentum = Vector3.zero;
                }
                else
                {
                    awaitingAirDirection = false;
                    airDirectionLocked = true;
                    airMomentum = dir * moveSpeed;
                }

                rb.velocity = airMomentum + Vector3.up * jumpForce;
            }

            // limpiamos flag si ya se canceló
            if (justCancelledAction) justCancelledAction = false;
        }
    }

    // ————————————————————————————————————————————————————————————————————————
    // 5) CÁMARA
    void TransitionCamera()
    {
        bool lowered = currentState == State.Crouch
                    || currentState == State.Slide
                    || currentState == State.Dropshot;

        Vector3 offset = currentState == State.Dropshot
                       ? dropshotCameraOffset
                       : slideCameraOffset;

        Vector3 target = lowered
            ? originalCameraPos + offset
            : originalCameraPos;

        cameraHolder.localPosition =
            Vector3.Lerp(cameraHolder.localPosition, target,
                         Time.deltaTime * cameraTransitionSpeed);
    }

    // ————————————————————————————————————————————————————————————————————————
    // GROUND CHECK
    private bool IsGrounded()
    {
        // Tú usabas OnCollisionStay/Exit; aquí lo simplifico:
        return Physics.Raycast(transform.position, Vector3.down, 1.1f);
    }
}
