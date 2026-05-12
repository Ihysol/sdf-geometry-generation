using UnityEngine;

public interface IVolumeRenderer
{
    /// <summary>Regenerates the renderer output for the supplied model.</summary>
    void Rebuild(VolumeModel model);

    /// <summary>Clears all generated renderer output.</summary>
    void Clear();
}
