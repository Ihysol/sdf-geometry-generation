using UnityEngine;

public abstract class VolumeChunkBase : MonoBehaviour
{
    public Bounds coreBounds;
    public Bounds buildBounds;

    public abstract void Rebuild(
        VolumeModel model,
        IScalarFieldSource source
    );

    public abstract void Clear();
}