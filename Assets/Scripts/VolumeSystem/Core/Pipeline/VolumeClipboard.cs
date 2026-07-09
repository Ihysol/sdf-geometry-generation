using UnityEngine;

public class VolumeClipboard : IVolumeClipboard
{
    public bool HasData { get; private set; }
    public BoundsInt Region { get; private set; }
    public float[] Density { get; private set; }
    public int[] Material { get; private set; }

    public static IVolumeClipboard Instance { get; private set; } = new VolumeClipboard();

    public void Copy(float[] density, int[] material, BoundsInt region)
    {
        Region = region;
        Density = density;
        Material = material;
        HasData = true;
    }

    public void Clear()
    {
        Density = null;
        Material = null;
        Region = new BoundsInt();
        HasData = false;
    }
}
