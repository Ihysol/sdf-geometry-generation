using UnityEngine;

/// <summary>
/// Activates when the player is submerged in water. Reduces speed and disables sprinting/jumping.
/// </summary>
public class SwimmingMovementComponent : MovementComponent
{
    [Header("Water Detection")]
    [Tooltip("Layer assigned to water colliders")]
    [SerializeField] LayerMask waterLayer;
    [SerializeField] float checkRadius = 0.5f;

    void Awake()
    {
        name = "Swimming";
        priority = 10f;
        speedMultiplier = 0.6f;
        canJump = false;
        canSprint = false;
    }

    public override bool CanActivate()
    {
        // Check if the player's upper body is inside a water collider
        Vector3 checkPoint = transform.position + Vector3.up * 0.5f;
        return Physics.CheckSphere(checkPoint, checkRadius, waterLayer);
    }

    public override void OnActivated()
    {
        Debug.Log("[Movement] Swimming activated");
        // Trigger underwater camera effects, disable footstep sounds, etc.
    }

    public override void OnDeactivated()
    {
        Debug.Log("[Movement] Swimming deactivated");
    }
}
