using UnityEngine;

/// <summary>
/// Base class for different movement types (e.g., Sprint, Wall-Run, Swim).
/// Attach multiple instances to the player and manage them via the ThirdPersonController.
/// </summary>
public abstract class MovementComponent : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Name of this movement type")]
    public string name = "Default";
    
    [Tooltip("Higher priority components will override lower ones when active")]
    public float priority = 0f;
    
    [Tooltip("Multiplier applied to base move speed")]
    public float speedMultiplier = 1f;
    
    [Tooltip("Whether jumping is allowed in this movement state")]
    public bool canJump = true;
    
    [Tooltip("Whether sprinting is allowed in this movement state")]
    public bool canSprint = true;

    /// <summary>
    /// Override to define conditions under which this movement becomes active.
    /// E.g., return true if player is in water, or holding a wall-run button.
    /// </summary>
    public virtual bool CanActivate() => true;

    /// <summary>
    /// Called when this movement component becomes the active one.
    /// Use this to trigger animations, sound effects, or state changes.
    /// </summary>
    public virtual void OnActivated() { }

    /// <summary>
    /// Called when this movement component is deactivated.
    /// </summary>
    public virtual void OnDeactivated() { }

    /// <summary>
    /// Invoked by the controller when a bound input action triggers.
    /// Return true if this component handled the command, false otherwise.
    /// </summary>
    public virtual bool ExecuteCommand(MovementCommand command) => false;
}
