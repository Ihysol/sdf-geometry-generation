using Unity.Collections;
using UnityEngine;

public class SubtractSphereOperation : IVolumeOperation
{
    public Vector3 Center { get; }
    public float Radius { get; }

    public BoundsInt AffectedRegion { get; private set; }
    public bool SupportsCpu => true;
    public bool SupportsGpu => true;

    public SubtractSphereOperation(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    public void ComputeAffectedRegion(VolumeLayout layout)
    {
        Vector3 minCell = (Center - new Vector3(Radius, Radius, Radius) - layout.Origin) / layout.CellSize;
        Vector3 maxCell = (Center + new Vector3(Radius, Radius, Radius) - layout.Origin) / layout.CellSize;

        int px = Mathf.FloorToInt(minCell.x);
        int py = Mathf.FloorToInt(minCell.y);
        int pz = Mathf.FloorToInt(minCell.z);
        int sx = Mathf.CeilToInt(maxCell.x) - px + 1;
        int sy = Mathf.CeilToInt(maxCell.y) - py + 1;
        int sz = Mathf.CeilToInt(maxCell.z) - pz + 1;

        AffectedRegion = new BoundsInt(px, py, pz, sx, sy, sz);
    }

    public void ApplyCpu(IVolumeBuffer buffer)
    {
        NativeArray<float> density = buffer.DensityCpu;
        VolumeLayout layout = buffer.Layout;
        float negRadius = -Radius;

        for (int z = AffectedRegion.position.z; z < AffectedRegion.position.z + AffectedRegion.size.z; z++)
        {
            if (z < 0 || z >= layout.Resolution.z) continue;
            for (int y = AffectedRegion.position.y; y < AffectedRegion.position.y + AffectedRegion.size.y; y++)
            {
                if (y < 0 || y >= layout.Resolution.y) continue;
                for (int x = AffectedRegion.position.x; x < AffectedRegion.position.x + AffectedRegion.size.x; x++)
                {
                    if (x < 0 || x >= layout.Resolution.x) continue;

                    Vector3Int index = new Vector3Int(x, y, z);
                    Vector3 world = layout.IndexToWorld(index);
                    float dist = (world - Center).magnitude + negRadius;

                    int offset = layout.IndexToOffset(index);
                    density[offset] = Mathf.Min(density[offset], dist);
                }
            }
        }
    }

    public void ApplyGpu(IVolumeBuffer buffer, UnityEngine.Rendering.CommandBuffer commandBuffer)
    {
        GpuOperationDispatcher.SubtractSphere(buffer, Center, Radius);
        buffer.SyncGpuToCpu();
    }
}
