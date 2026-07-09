using Unity.Collections;
using UnityEngine;

public class CopyOperation : IVolumeOperation
{
    public BoundsInt Region { get; }
    public BoundsInt AffectedRegion => Region;
    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    private readonly IVolumeClipboard _clipboard;

    public CopyOperation(BoundsInt region, IVolumeClipboard clipboard = null)
    {
        Region = region;
        _clipboard = clipboard ?? VolumeClipboard.Instance;
    }

    public void ApplyCpu(IVolumeBuffer buffer)
    {
        NativeArray<float> density = buffer.DensityCpu;
        NativeArray<int> material = buffer.MaterialCpu;
        VolumeLayout layout = buffer.Layout;

        int sizeX = Mathf.Max(0, Region.size.x);
        int sizeY = Mathf.Max(0, Region.size.y);
        int sizeZ = Mathf.Max(0, Region.size.z);

        float[] densityArr = new float[sizeX * sizeY * sizeZ];
        int[] materialArr = new int[sizeX * sizeY * sizeZ];

        for (int z = 0; z < sizeZ; z++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int srcX = Region.position.x + x;
                    int srcY = Region.position.y + y;
                    int srcZ = Region.position.z + z;

                    if (srcX < 0 || srcX >= layout.Resolution.x) continue;
                    if (srcY < 0 || srcY >= layout.Resolution.y) continue;
                    if (srcZ < 0 || srcZ >= layout.Resolution.z) continue;

                    int offset = layout.IndexToOffset(new Vector3Int(srcX, srcY, srcZ));
                    int dstIndex = z * sizeX * sizeY + y * sizeX + x;

                    densityArr[dstIndex] = density[offset];
                    materialArr[dstIndex] = material[offset];
                }
            }
        }

        _clipboard.Copy(densityArr, materialArr, Region);
    }

    public void ApplyGpu(IVolumeBuffer buffer, UnityEngine.Rendering.CommandBuffer commandBuffer)
    {
        throw new System.NotImplementedException("GPU operations not yet implemented.");
    }
}
