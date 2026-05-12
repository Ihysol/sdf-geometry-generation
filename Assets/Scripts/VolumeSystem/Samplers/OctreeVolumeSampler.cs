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
}
