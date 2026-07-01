using UnityEngine;

/// <summary>
/// Activates when the player is close to a wall and moving forward. Allows faster movement but disables sprinting.
/// </summary>
public class WallRunMovementComponent : MovementComponent
{
    [Header("Wall Detection")]
    [Tooltip("Layer assigned to walkable walls")]
    [SerializeField] LayerMask wallLayer;
    [SerializeField] float wallCheckDistance = 0.8f;

    void Awake()
    {
        name = "Wall Run";
        priority = 5f;
        speedMultiplier = 1.2f;
        canJump = true; // Allow jumping off the wall
        canSprint = false;
    }

    public override bool CanActivate()
    {
        // Simple forward raycast to detect wall proximity
        Vector3 forward = transform.forward;
        return Physics.Raycast(transform.position, forward, out RaycastHit hit, wallCheckDistance, wallLayer);
    }

    public override void OnActivated()
    {
        Debug.Log("[Movement] Wall Run activated");
        // Trigger wall-run animation or sound effects
    }

    public override void OnDeactivated()
    {
        Debug.Log("[Movement] Wall Run deactivated");
    }

    public override bool ExecuteCommand(MovementCommand command)
    {
        switch (command)
        {
            case MovementCommand.WallJump:
                PerformWallJump();
                return true; // Handled
            default:
                return false; // Not handled by this component
        }
    }

    void PerformWallJump()
    {
        Debug.Log("[WallRun] Executing Wall Jump!");
        // Add wall jump physics, animation triggers, etc. here
        // Example: body.AddForce(Vector3.up * 5f + transform.right * 3f, ForceMode.Impulse);
    }
}
