using UnityEngine;

public interface IVolumeClipboard
{
    bool HasData { get; }
    BoundsInt Region { get; }
    float[] Density { get; }
    int[] Material { get; }

    void Copy(float[] density, int[] material, BoundsInt region);
    void Clear();
}
