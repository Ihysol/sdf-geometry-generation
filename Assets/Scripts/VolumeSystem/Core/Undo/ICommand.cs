using UnityEngine;

/// <summary>Single undoable operation. Execute applies state, Revoke restores previous.</summary>
public interface ICommand
{
    /// <summary>Apply this command's changes.</summary>
    void Execute();

    /// <summary>Restore the state before Execute() was called.</summary>
    void Revoke();

    /// <summary>World-space bounds affected by this command (for dirty chunk tracking).</summary>
    Bounds AffectedBounds { get; }
}
