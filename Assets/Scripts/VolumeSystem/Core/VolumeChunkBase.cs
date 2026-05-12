using UnityEngine;

public abstract class VolumeChunkBase : MonoBehaviour
{
    public Bounds coreBounds;
    public Bounds buildBounds;

    /// <summary>Regenerates this chunk mesh from the model and scalar field.</summary>
    public abstract void Rebuild(
        VolumeModel model,
        IScalarFieldSource source
    );

    /// <summary>Clears this chunk's generated mesh data.</summary>
    public abstract void Clear();
}
