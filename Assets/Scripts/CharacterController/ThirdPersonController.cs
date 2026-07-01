using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

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
    [SerializeField] float groundCheckOffset = 0.1f;

    [Header("Camera")]
    [SerializeField] Transform orbitCamera;
    [SerializeField] float cameraDistance = 10f;
    [SerializeField] float cameraHeightOffset = 2.5f;
    [SerializeField] float lookSensitivity = 2f;
    [Tooltip("How fast the camera orbits. Lower = smoother, higher = snappier")]
    [SerializeField] float cameraTurnSpeed = 8f;
    [SerializeField] float minVerticalAngle = -60f;
    [SerializeField] float maxVerticalAngle = 75f;

    [Header("Input")]
    [Tooltip("Assign your Player InputActionAsset here. Controls will not work without it.")]
    [SerializeField] InputActionAsset inputActions;

    [System.Serializable]
    public class CommandBinding
    {
        public string actionName;
        public MovementCommand command;
    }

    [Header("Command Bindings")]
    [Tooltip("Map Input Action names to MovementCommands. Rebind keys in the Input System window.")]
    [SerializeField] List<CommandBinding> commandBindings = new();

    [Header("Movement System")]
    [Tooltip("List of movement components. Higher priority components override lower ones when active.")]
    [SerializeField] List<MovementComponent> movementComponents = new();
    MovementComponent activeMovement;

    Rigidbody body;
    CapsuleCollider capsule;
    Vector2 moveInput;
    bool jumpPressed;
    bool isRunning;
    bool isGrounded;
    float targetYaw;
    float targetPitch;
    float currentYaw;
    float currentPitch;
    bool cursorLocked;
    bool inputBound;

    InputAction actionMove;
    InputAction actionLook;
    InputAction actionJump;
    InputAction actionSprint;
    
    Dictionary<string, InputAction> boundCommandActions = new();
    Dictionary<InputAction, System.Action<InputAction.CallbackContext>> commandCallbacks = new();
    InputActionMap activePlayerMap;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        if (orbitCamera == null)
        {
            orbitCamera = Camera.main?.transform;
        }

        if (orbitCamera != null)
        {
            Vector3 toCamera = orbitCamera.position - transform.position;
            toCamera.y = 0f;
            targetYaw = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg;
            currentYaw = targetYaw;
            
            float pitchDistance = cameraDistance * 0.5f + 0.001f;
            targetPitch = Mathf.Asin((orbitCamera.position.y - transform.position.y - cameraHeightOffset) / pitchDistance) * Mathf.Rad2Deg;
            targetPitch = Mathf.Clamp(targetPitch, minVerticalAngle, maxVerticalAngle);
            currentPitch = targetPitch;
        }
    }

    void OnEnable()
    {
        if (inputBound) return;

        InputActionAsset asset = inputActions;
        if (asset == null)
        {
            Debug.LogWarning("[ThirdPersonController] No InputActionAsset assigned. Drag your Player Input Actions into the Inspector to enable controls.");
            return;
        }

        activePlayerMap = asset.FindActionMap("Player") ?? asset.FindActionMap("Default");
        if (activePlayerMap == null)
        {
            Debug.LogWarning("[ThirdPersonController] Could not find 'Player' or 'Default' Action Map in the assigned InputActionAsset.");
            return;
        }

        activePlayerMap.Enable();

        actionMove = activePlayerMap.FindAction("Move");
        actionLook = activePlayerMap.FindAction("Look");
        actionJump = activePlayerMap.FindAction("Jump");
        actionSprint = activePlayerMap.FindAction("Sprint");

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

        // Bind command actions with proper callback references
        foreach (var binding in commandBindings)
        {
            var action = activePlayerMap.FindAction(binding.actionName);
            if (action != null)
            {
                action.Enable();
                System.Action<InputAction.CallbackContext> callback = ctx => DispatchCommand(binding.command);
                action.started += callback;
                boundCommandActions[binding.actionName] = action;
                commandCallbacks[action] = callback;
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

        foreach (var kvp in commandCallbacks)
        {
            kvp.Key.started -= kvp.Value;
            kvp.Key.Disable();
        }
        commandCallbacks.Clear();
        boundCommandActions.Clear();

        if (activePlayerMap != null)
            activePlayerMap.Disable();

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
        targetYaw += delta.x * lookSensitivity;
        targetPitch -= delta.y * lookSensitivity;
        targetPitch = Mathf.Clamp(targetPitch, minVerticalAngle, maxVerticalAngle);
    }

    void DispatchCommand(MovementCommand command)
    {
        if (activeMovement != null && activeMovement.ExecuteCommand(command))
        {
            return; // Command was handled by the active component
        }

        // Fallback: try other components if active one doesn't support it
        foreach (var comp in movementComponents)
        {
            if (comp != null && comp.ExecuteCommand(command))
                break;
        }
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

        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, cameraTurnSpeed * Time.deltaTime);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, cameraTurnSpeed * Time.deltaTime);

        UpdateCameraPosition();
        isGrounded = CheckGrounded();
        
        UpdateMovementSystem();
    }

    void FixedUpdate()
    {
        HandleMovement();
        HandleJump();
    }

    void UpdateMovementSystem()
    {
        if (movementComponents == null) return;

        MovementComponent best = null;
        foreach (var comp in movementComponents)
        {
            if (comp != null && comp.CanActivate())
            {
                if (best == null || comp.priority > best.priority)
                {
                    best = comp;
                }
            }
        }

        if (activeMovement != best)
        {
            if (activeMovement != null) activeMovement.OnDeactivated();
            activeMovement = best;
            if (activeMovement != null) activeMovement.OnActivated();
        }
    }

    void UpdateCameraPosition()
    {
        if (orbitCamera == null) return;

        float camX = transform.position.x + Mathf.Sin(Mathf.Deg2Rad * currentYaw) * cameraDistance;
        float camZ = transform.position.z + Mathf.Cos(Mathf.Deg2Rad * currentYaw) * cameraDistance;
        float camY = transform.position.y + cameraHeightOffset + Mathf.Sin(Mathf.Deg2Rad * currentPitch) * cameraDistance * 0.5f;

        Vector3 targetPosition = new Vector3(camX, camY, camZ);
        orbitCamera.position = targetPosition;
        orbitCamera.LookAt(transform.position + Vector3.up * cameraHeightOffset);
    }

    void HandleMovement()
    {
        if (moveInput == Vector2.zero) return;

        float multiplier = activeMovement?.speedMultiplier ?? 1f;
        bool canSprint = activeMovement?.canSprint ?? true;
        
        // Only apply sprint multiplier if input is held AND current movement allows it
        bool effectiveRunning = isRunning && canSprint;
        float currentSpeed = moveSpeed * (effectiveRunning ? runMultiplier : 1f) * multiplier;

        Vector3 cameraForward = new Vector3(Mathf.Sin(Mathf.Deg2Rad * currentYaw), 0f, Mathf.Cos(Mathf.Deg2Rad * currentYaw));
        Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward);

        Vector3 moveDirection = (-cameraForward * moveInput.y - cameraRight * moveInput.x).normalized;

        body.linearVelocity = new Vector3(
            moveDirection.x * currentSpeed,
            body.linearVelocity.y,
            moveDirection.z * currentSpeed
        );

        float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
        Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
    }

    void HandleJump()
    {
        if (!jumpPressed || !isGrounded) return;
        
        bool canJump = activeMovement?.canJump ?? true;
        if (!canJump) return;

        body.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        jumpPressed = false;
    }

    bool CheckGrounded()
    {
        float radius = capsule.radius * 0.9f;
        Vector3 center = transform.position + capsule.center;
        Vector3 down = center - Vector3.up * (capsule.height * 0.5f + groundCheckOffset);
        return Physics.CheckSphere(down, radius, groundLayer);
    }
}
