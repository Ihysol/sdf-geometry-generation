   using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Rigidbody))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float runMultiplier = 1.6f;
    [SerializeField] float rotationSpeed = 10f;

    [Header("Jumping")]
    [SerializeField] float jumpForce = 7f;
    [SerializeField] LayerMask groundLayer;

    [Header("Camera")]
    [SerializeField] Transform orbitCamera;
    [SerializeField] float cameraDistance = 10f;
    [SerializeField] float cameraHeightOffset = 2.5f;
    [SerializeField] float lookSensitivity = 2f;
    [SerializeField] float minVerticalAngle = -60f;
    [SerializeField] float maxVerticalAngle = 75f;

    Rigidbody rigidbody;
    CapsuleCollider capsule;
    Vector2 moveInput;
    bool jumpPressed;
    bool isRunning;
    bool isGrounded;
    float cameraYaw;
    float cameraPitch;
    bool cursorLocked;
    bool inputBound;

    InputActionAsset inputActions;
    InputAction actionMove;
    InputAction actionLook;
    InputAction actionJump;
    InputAction actionSprint;

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        rigidbody.freezeRotation = true;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        if (orbitCamera == null)
        {
            orbitCamera = Camera.main?.transform;
        }

        if (orbitCamera != null)
        {
            Vector3 toCamera = orbitCamera.position - transform.position;
            toCamera.y = 0f;
            cameraYaw = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg;
            cameraPitch = Mathf.Asin((orbitCamera.position.y - transform.position.y - cameraHeightOffset) / (cameraDistance * 0.5f + 0.001f)) * Mathf.Rad2Deg;
            cameraPitch = Mathf.Clamp(cameraPitch, minVerticalAngle, maxVerticalAngle);
        }
    }

    void OnEnable()
    {
        if (inputBound) return;

        if (inputActions == null)
        {
            inputActions = Resources.Load<InputActionAsset>("InputSystem_Actions");
        }

        if (inputActions != null)
        {
            var playerMap = inputActions.FindActionMap("Player");
            if (playerMap != null)
            {
                actionMove = playerMap.FindAction("Move");
                actionLook = playerMap.FindAction("Look");
                actionJump = playerMap.FindAction("Jump");
                actionSprint = playerMap.FindAction("Sprint");

                if (actionMove != null)
                {
                    actionMove.Enable();
                    actionMove.performed += OnMovePerformed;
                    actionMove.canceled += OnMoveCanceled;
                }
                if (actionLook != null)
                {
                    actionLook.Enable();
                    actionLook.performed += OnLookPerformed;
                    actionLook.canceled += OnLookCanceled;
                }
                if (actionJump != null)
                {
                    actionJump.Enable();
                    actionJump.started += OnJumpStarted;
                    actionJump.canceled += OnJumpCanceled;
                }
                if (actionSprint != null)
                {
                    actionSprint.Enable();
                    actionSprint.started += OnSprintStarted;
                    actionSprint.canceled += OnSprintCanceled;
                }
            }
        }

        inputBound = true;
    }

    void OnDisable()
    {
        if (actionMove != null)
        {
            actionMove.performed -= OnMovePerformed;
            actionMove.canceled -= OnMoveCanceled;
            actionMove.Disable();
        }
        if (actionLook != null)
        {
            actionLook.performed -= OnLookPerformed;
            actionLook.canceled -= OnLookCanceled;
            actionLook.Disable();
        }
        if (actionJump != null)
        {
            actionJump.started -= OnJumpStarted;
            actionJump.canceled -= OnJumpCanceled;
            actionJump.Disable();
        }
        if (actionSprint != null)
        {
            actionSprint.started -= OnSprintStarted;
            actionSprint.canceled -= OnSprintCanceled;
            actionSprint.Disable();
        }

        inputBound = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void OnMovePerformed(InputAction.CallbackContext ctx) => moveInput = ctx.ReadValue<Vector2>();
    void OnMoveCanceled(InputAction.CallbackContext ctx) => moveInput = Vector2.zero;
    void OnLookPerformed(InputAction.CallbackContext ctx) => ApplyLook(ctx.ReadValue<Vector2>());
    void OnLookCanceled(InputAction.CallbackContext ctx) { }
    void OnJumpStarted(InputAction.CallbackContext ctx) => jumpPressed = true;
    void OnJumpCanceled(InputAction.CallbackContext ctx) => jumpPressed = false;
    void OnSprintStarted(InputAction.CallbackContext ctx) => isRunning = true;
    void OnSprintCanceled(InputAction.CallbackContext ctx) => isRunning = false;

    void ApplyLook(Vector2 delta)
    {
        cameraYaw += delta.x * lookSensitivity;
        cameraPitch -= delta.y * lookSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch, minVerticalAngle, maxVerticalAngle);
    }

    void Update()
    {
        if (!cursorLocked)
        {
            cursorLocked = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return;
        }

        UpdateCameraPosition();
        isGrounded = CheckGrounded();
    }

    void FixedUpdate()
    {
        HandleMovement();
        HandleJump();
    }

    void UpdateCameraPosition()
    {
        if (orbitCamera == null) return;

        float targetX = transform.position.x + Mathf.Sin(Mathf.Deg2Rad * cameraYaw) * cameraDistance;
        float targetZ = transform.position.z + Mathf.Cos(Mathf.Deg2Rad * cameraYaw) * cameraDistance;
        float targetY = transform.position.y + cameraHeightOffset + Mathf.Sin(Mathf.Deg2Rad * cameraPitch) * cameraDistance * 0.5f;

        Vector3 targetPosition = new Vector3(targetX, targetY, targetZ);
        orbitCamera.position = targetPosition;
        orbitCamera.LookAt(transform.position + Vector3.up * cameraHeightOffset);
    }

    void HandleMovement()
    {
        if (moveInput == Vector2.zero) return;

        float currentSpeed = moveSpeed * (isRunning ? runMultiplier : 1f);

        Vector3 cameraForward = new Vector3(Mathf.Sin(Mathf.Deg2Rad * cameraYaw), 0f, Mathf.Cos(Mathf.Deg2Rad * cameraYaw));
        Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward);

        Vector3 moveDirection = (-cameraForward * moveInput.y - cameraRight * moveInput.x).normalized;

        rigidbody.linearVelocity = new Vector3(
            moveDirection.x * currentSpeed,
            rigidbody.linearVelocity.y,
            moveDirection.z * currentSpeed
        );

        float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
        Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
    }

    void HandleJump()
    {
        if (!jumpPressed || !isGrounded) return;
        rigidbody.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        jumpPressed = false;
    }

    bool CheckGrounded()
    {
        float radius = capsule.radius * 0.9f;
        Vector3 center = capsule.bounds.center;
        Vector3 down = center - Vector3.up * (capsule.height * 0.5f + 0.05f);
        return Physics.CheckSphere(down, radius, groundLayer);
    }
}
