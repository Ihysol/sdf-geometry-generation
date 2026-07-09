using Unity.Collections;
using UnityEngine;

public class PasteOperation : IVolumeOperation
{
    public Vector3Int Position { get; }
    public BoundsInt AffectedRegion { get; private set; }
    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    private readonly float[] _density;
    private readonly int[] _material;
    private readonly Vector3Int _sourceSize;

    public PasteOperation(Vector3Int position, IVolumeClipboard clipboard)
    {
        if (!clipboard.HasData)
            throw new System.InvalidOperationException("Clipboard is empty.");

        Position = position;
        _density = clipboard.Density;
        _material = clipboard.Material;
        _sourceSize = clipboard.Region.size;
        AffectedRegion = new BoundsInt(Position, _sourceSize);
    }

    public void ApplyCpu(IVolumeBuffer buffer)
    {
        NativeArray<float> density = buffer.DensityCpu;
        NativeArray<int> material = buffer.MaterialCpu;
        VolumeLayout layout = buffer.Layout;

        int sizeX = Mathf.Max(0, _sourceSize.x);
        int sizeY = Mathf.Max(0, _sourceSize.y);
        int sizeZ = Mathf.Max(0, _sourceSize.z);

        for (int z = 0; z < sizeZ; z++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int dstX = Position.x + x;
                    int dstY = Position.y + y;
                    int dstZ = Position.z + z;

                    if (dstX < 0 || dstX >= layout.Resolution.x) continue;
                    if (dstY < 0 || dstY >= layout.Resolution.y) continue;
                    if (dstZ < 0 || dstZ >= layout.Resolution.z) continue;

                    int offset = layout.IndexToOffset(new Vector3Int(dstX, dstY, dstZ));
                    int srcIndex = z * sizeX * sizeY + y * sizeX + x;

                    density[offset] = _density[srcIndex];
                    material[offset] = _material[srcIndex];
                }
            }
        }
    }

    public void ApplyGpu(IVolumeBuffer buffer, UnityEngine.Rendering.CommandBuffer commandBuffer)
    {
        throw new System.NotImplementedException("GPU operations not yet implemented.");
    }
}
