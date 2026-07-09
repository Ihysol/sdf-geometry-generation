using Unity.Collections;
using UnityEngine;

public class SmoothOperation : IVolumeOperation
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public int Iterations { get; }

    public BoundsInt AffectedRegion { get; private set; }
    public bool SupportsCpu => true;
    public bool SupportsGpu => true;

    public SmoothOperation(Vector3 center, float radius, int iterations = 1)
    {
        Center = center;
        Radius = radius;
        Iterations = Mathf.Max(1, iterations);
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
        float radiusSq = Radius * Radius;

        NativeArray<float> tempDensity = new NativeArray<float>(density.Length, Allocator.Temp);

        for (int iter = 0; iter < Iterations; iter++)
        {
            tempDensity.CopyFrom(density);

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

                        if ((world - Center).sqrMagnitude > radiusSq * 2f)
                            continue;

                        int offset = layout.IndexToOffset(index);
                        float sum = 0f;
                        int count = 0;

                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    Vector3Int nb = new Vector3Int(x + dx, y + dy, z + dz);
                                    if (!layout.IsInside(nb)) continue;

                                    sum += tempDensity[layout.IndexToOffset(nb)];
                                    count++;
                                }
                            }
                        }

                        density[offset] = sum / count;
                    }
                }
            }
        }

        tempDensity.Dispose();
    }

   public void ApplyGpu(IVolumeBuffer buffer, UnityEngine.Rendering.CommandBuffer commandBuffer)
    {
        for (int i = 0; i < Iterations; i++)
        {
            GpuOperationDispatcher.Smooth(buffer, Center, Radius);
        }
    }
}
