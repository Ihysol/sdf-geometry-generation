public interface IVolumeSampler
{
    IVolumeData Volume { get; }
    bool IsDirty { get; }

    /// <summary>Marks the sampled volume as stale.</summary>
    void MarkDirty();

    /// <summary>Rebuilds the sampled volume from the given field source.</summary>
    void RebuildVolume(IScalarFieldSource source);
}
