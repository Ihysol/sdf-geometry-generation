using UnityEngine;

[System.Serializable]
public class OctreeVolumeSampler : VolumeSamplerBase<OctreeVolume>
{
    public Vector3 center = Vector3.zero;
    public Vector3 extent = new Vector3(4, 4, 4);

    public OctreeVolumeBuilder builder = new OctreeVolumeBuilder();
    public DirectFlatOctreeVolumeBuilder directFlatBuilder = new DirectFlatOctreeVolumeBuilder();
    public string LastIncrementalFallbackReason { get; private set; } = string.Empty;

    /// <summary>Rebuilds the octree volume from the given scalar field.</summary>
    public override void RebuildVolume(IScalarFieldSource source)
    {
        if (source == null)
        {
            Debug.LogWarning("OctreeVolumeSampler: No source assigned.");
            Volume = null;
            return;
        }

        builder.center = center;
        builder.size = extent;

        Volume = builder.Build(source);

        IsDirty = false;
    }

    public void RebuildFlatVolume(IScalarFieldSource source)
    {
        if (source == null)
        {
            Debug.LogWarning("OctreeVolumeSampler: No source assigned.");
            Volume = null;
            return;
        }

        directFlatBuilder.center = center;
        directFlatBuilder.size = extent;

        Volume = directFlatBuilder.Build(source);
        IsDirty = false;
    }

    public bool RebuildVolumeRegion(IScalarFieldSource source, Bounds dirtyBounds)
    {
        LastIncrementalFallbackReason = string.Empty;

        if (source == null)
        {
            Debug.LogWarning("OctreeVolumeSampler: No source assigned.");
            Volume = null;
            IsDirty = false;
            LastIncrementalFallbackReason = "source-null";
            return false;
        }

        builder.center = center;
        builder.size = extent;

        if (Volume == null)
        {
            LastIncrementalFallbackReason = "volume-null";
            return false;
        }

        if (!builder.RebuildRegion(Volume, source, dirtyBounds, out OctreeVolume rebuilt) || rebuilt == null)
        {
            LastIncrementalFallbackReason = rebuilt == null ? "builder-returned-null" : "builder-rejected-region";
            return false;
        }

        Volume = rebuilt;
        IsDirty = false;
        return true;
    }
}
