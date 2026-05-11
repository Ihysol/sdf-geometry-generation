public interface IVolumeSampler
{
    IVolumeData Volume { get; }
    bool IsDirty { get; }

    void MarkDirty();
    void RebuildVolume(IScalarFieldSource source);
}