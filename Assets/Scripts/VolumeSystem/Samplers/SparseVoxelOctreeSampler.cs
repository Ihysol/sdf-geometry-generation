using UnityEngine;

[System.Serializable]
public class SparseVoxelOctreeSampler : VolumeSamplerBase<SparseVoxelOctreeVolume>
{
    public Vector3 center = Vector3.zero;
    public Vector3 extent = new Vector3(4, 4, 4);
    public SparseVoxelOctreeBuilder builder = new SparseVoxelOctreeBuilder();

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
        if (source == null)
        {
            Volume = null;
            IsDirty = false;
            return false;
        }

        builder.backend.center = center;
        builder.backend.size = extent;

        if (Volume == null)
        {
            RebuildVolume(source);
            return false;
        }

        if (!builder.RebuildRegion(Volume, source, dirtyBounds, out SparseVoxelOctreeVolume rebuilt) || rebuilt == null)
        {
            RebuildVolume(source);
            return false;
        }

        Volume = rebuilt;
        IsDirty = false;
        return true;
    }
}
