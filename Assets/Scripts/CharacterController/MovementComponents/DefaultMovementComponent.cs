using UnityEngine;

/// <summary>
/// Standard ground movement. Always active unless overridden by a higher-priority component.
/// </summary>
public class DefaultMovementComponent : MovementComponent
{
    void Awake()
    {
        name = "Default";
        priority = 0f;
        speedMultiplier = 1f;
        canJump = true;
        canSprint = true;
    }

    public override bool CanActivate() => true;

    public override void OnActivated()
    {
        Debug.Log("[Movement] Default movement activated");
    }

    public override void OnDeactivated()
    {
        Debug.Log("[Movement] Default movement deactivated");
    }
}
