using UnityEngine;

public class InitialBufferBuilder
{
    private VolumeLayout _layout;

    public InitialBufferBuilder(VolumeLayout layout)
    {
        _layout = layout;
    }

    public void Build(IVolumeSource source, IVolumeBuffer buffer)
    {
        if (source == null || buffer == null)
            return;

        var density = buffer.DensityCpu;
        var material = buffer.MaterialCpu;

        double buildStart = Time.realtimeSinceStartup * 1000.0;
        int totalCells = _layout.Resolution.x * _layout.Resolution.y * _layout.Resolution.z;

        for (int z = 0; z < _layout.Resolution.z; z++)
        {
            for (int y = 0; y < _layout.Resolution.y; y++)
            {
                for (int x = 0; x < _layout.Resolution.x; x++)
                {
                    Vector3Int index = new Vector3Int(x, y, z);
                    Vector3 world = _layout.IndexToWorld(index);

                    int offset = _layout.IndexToOffset(index);
                    float val = source.Sample(world);
                    if (float.IsInfinity(val) || float.IsNaN(val))
                        val = 1f;
                    density[offset] = val;
                    material[offset] = source.GetMaterial(world);
                }
            }
        }

        buffer.SyncState = BufferSyncState.CpuDirty;

        double elapsed = (Time.realtimeSinceStartup * 1000.0) - buildStart;
        Debug.Log($"[Buffer] Build complete: {totalCells} cells, {elapsed:F0}ms");
    }

    public void BuildPartial(IVolumeSource source, IVolumeBuffer buffer, BoundsInt region)
    {
        if (source == null || buffer == null)
            return;

        var density = buffer.DensityCpu;
        var material = buffer.MaterialCpu;

        for (int z = region.position.z; z < region.position.z + region.size.z; z++)
        {
            if (z < 0 || z >= _layout.Resolution.z) continue;
            for (int y = region.position.y; y < region.position.y + region.size.y; y++)
            {
                if (y < 0 || y >= _layout.Resolution.y) continue;
                for (int x = region.position.x; x < region.position.x + region.size.x; x++)
                {
                    if (x < 0 || x >= _layout.Resolution.x) continue;

                    Vector3Int index = new Vector3Int(x, y, z);
                    Vector3 world = _layout.IndexToWorld(index);

                    int offset = _layout.IndexToOffset(index);
                    float val = source.Sample(world);
                    if (float.IsInfinity(val) || float.IsNaN(val))
                        val = 1f;
                    density[offset] = val;
                    material[offset] = source.GetMaterial(world);
                }
            }
        }

        buffer.SyncState = BufferSyncState.CpuDirty;
    }
}
