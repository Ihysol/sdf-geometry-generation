using Unity.Collections;
using UnityEngine;

public class PaintMaterialOperation : IVolumeOperation
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public int MaterialId { get; }

    public BoundsInt AffectedRegion { get; private set; }
    public bool SupportsCpu => true;
    public bool SupportsGpu => true;

    public PaintMaterialOperation(Vector3 center, float radius, int materialId)
    {
        Center = center;
        Radius = radius;
        MaterialId = materialId;
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
        NativeArray<int> material = buffer.MaterialCpu;
        VolumeLayout layout = buffer.Layout;
        float radiusSq = Radius * Radius;

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
                    float distSq = (world - Center).sqrMagnitude;

                    if (distSq <= radiusSq)
                    {
                        int offset = layout.IndexToOffset(index);
                        material[offset] = MaterialId;
                    }
                }
            }
        }
    }

    public void ApplyGpu(IVolumeBuffer buffer, UnityEngine.Rendering.CommandBuffer commandBuffer)
    {
        GpuOperationDispatcher.PaintMaterial(buffer, Center, Radius, MaterialId);
        buffer.SyncGpuToCpu();
    }
}
