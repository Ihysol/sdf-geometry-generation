using UnityEngine;

[System.Serializable]
public class VoxelGridSampler : IVolumeSampler
{
    [Header("Builder")]
    public VoxelGridBuilder builder = new();

    public VoxelGrid Volume { get; private set; }

    IVolumeData IVolumeSampler.Volume => Volume;

    public bool IsDirty { get; private set; } = true;

    /// <summary>Marks the sampled voxel grid as stale.</summary>
    public void MarkDirty()
    {
        IsDirty = true;
    }

    /// <summary>Rebuilds the voxel grid from the given scalar field.</summary>
    public void RebuildVolume(IScalarFieldSource source)
    {
        if (source == null)
        {
            Volume = null;
            IsDirty = false;
            return;
        }

        builder?.Validate();

        Volume = builder.Build(source);
        IsDirty = false;
    }
}
