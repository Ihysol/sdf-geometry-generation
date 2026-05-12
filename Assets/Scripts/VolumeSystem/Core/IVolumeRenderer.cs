using UnityEngine;

public interface IVolumeRenderer
{
    void Rebuild(VolumeModel model);
    void Clear();
}