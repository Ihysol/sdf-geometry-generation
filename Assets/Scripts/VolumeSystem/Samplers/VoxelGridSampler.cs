using UnityEngine;

[System.Serializable]
public class VoxelGridSampler : IVolumeSampler
{
    [Header("Builder")]
    public VoxelGridBuilder builder = new();

    public VoxelGrid Volume { get; private set; }

    IVolumeData IVolumeSampler.Volume => Volume;

    public bool IsDirty { get; private set; } = true;

    public void MarkDirty()
    {
        IsDirty = true;
    }

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