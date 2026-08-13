using UnityEngine;

/// <summary>ADR-004: Semantic edit type — determines replay behavior.</summary>
public enum EditType
{
    Carve,
    Fill,
    Smooth,
    PaintMaterial
}

/// <summary>Read-only view into a volume region for operation replay. See ADR-008.</summary>
public interface IVolumeView
{
    VolumeLayout Layout { get; }
    float GetDensity(int x, int y, int z);
    void SetDensity(int x, int y, int z, float value);
}

/// <summary>ADR-004: Base class for persistent edit operations. Each subclass implements Replay + Inverse.</summary>
public abstract class PersistentEditOperation
{
    public EditType Type { get; protected set; }
    
    /// <summary>Region in the operation's anchor coordinate space (not world).</summary>
    public Bounds Region { get; protected set; }
    
    /// <summary>Determines how Region transforms when objects/processor move. See ADR-015.</summary>
    public EditAnchor Anchor { get; protected set; }

    /// <summary>Monotonic generation — higher means newer. Used for deterministic replay order.</summary>
    public int Generation { get; set; }

    /// <summary>Replay this operation over the target volume region.</summary>
    public abstract void Replay(IVolumeView target);

    /// <summary>Create an inverse that undoes this operation's effect. Returns null if non-invertible.</summary>
    public abstract PersistentEditOperation Inverse();

    /// <summary>Check if this operation intersects the given world-space bounds (anchor-resolved).</summary>
    public bool IntersectsWorld(Bounds worldBounds, Transform processorTransform)
    {
        if (!Anchor.ResolveRegion(Region, processorTransform, out var resolved))
            return false; // Suspended — treat as non-intersecting until recovered

        Bounds a = resolved;
        Bounds b = worldBounds;
        return !(a.max.x < b.min.x || a.min.x > b.max.x ||
                 a.max.y < b.min.y || a.min.y > b.max.y ||
                 a.max.z < b.min.z || a.min.z > b.max.z);
    }
}
