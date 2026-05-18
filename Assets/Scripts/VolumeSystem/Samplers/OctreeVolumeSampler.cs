using UnityEngine;

[System.Serializable]
public class OctreeVolumeSampler : IVolumeSampler
{
    public Vector3 center = Vector3.zero;
    public Vector3 extent = new Vector3(4, 4, 4);

    public OctreeVolumeBuilder builder = new OctreeVolumeBuilder();

    public OctreeVolume Volume { get; private set; }

    IVolumeData IVolumeSampler.Volume => Volume;

    public bool IsDirty { get; private set; } = true;

    /// <summary>Marks the sampled octree as stale.</summary>
    public void MarkDirty()
    {
        IsDirty = true;
    }

    /// <summary>Rebuilds the octree volume from the given scalar field.</summary>
    public void RebuildVolume(IScalarFieldSource source)
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

    public bool RebuildVolumeRegion(IScalarFieldSource source, Bounds dirtyBounds)
    {
        if (source == null)
        {
            Debug.LogWarning("OctreeVolumeSampler: No source assigned.");
            Volume = null;
            IsDirty = false;
            return false;
        }

        builder.center = center;
        builder.size = extent;

        if (Volume == null)
        {
            RebuildVolume(source);
            return false;
        }

        if (!builder.RebuildRegion(Volume, source, dirtyBounds, out OctreeVolume rebuilt) || rebuilt == null)
        {
            RebuildVolume(source);
            return false;
        }

        Volume = rebuilt;
        IsDirty = false;
        return true;
    }
}
