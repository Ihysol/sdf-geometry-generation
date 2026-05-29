using UnityEngine;

[System.Serializable]
public class SparseVoxelOctreeSampler : VolumeSamplerBase<SparseVoxelOctreeVolume>
{
    public Vector3 center = Vector3.zero;
    public Vector3 extent = new Vector3(4, 4, 4);
    public SparseVoxelOctreeBuilder builder = new SparseVoxelOctreeBuilder();
    public string LastIncrementalFallbackReason { get; private set; } = string.Empty;

    public override void RebuildVolume(IScalarFieldSource source)
    {
        if (source == null)
        {
            Volume = null;
            IsDirty = false;
            return;
        }

        builder.backend.center = center;
        builder.backend.size = extent;
        Volume = builder.Build(source);
        IsDirty = false;
    }

    public bool RebuildVolumeRegion(IScalarFieldSource source, Bounds dirtyBounds)
    {
        LastIncrementalFallbackReason = string.Empty;

        if (source == null)
        {
            Volume = null;
            IsDirty = false;
            LastIncrementalFallbackReason = "source-null";
            return false;
        }

        builder.backend.center = center;
        builder.backend.size = extent;

        if (Volume == null)
        {
            LastIncrementalFallbackReason = "volume-null";
            return false;
        }

        if (!builder.RebuildRegion(Volume, source, dirtyBounds, out SparseVoxelOctreeVolume rebuilt) || rebuilt == null)
        {
            LastIncrementalFallbackReason = rebuilt == null ? "builder-returned-null" : "builder-rejected-region";
            return false;
        }

        Volume = rebuilt;
        IsDirty = false;
        return true;
    }
}
