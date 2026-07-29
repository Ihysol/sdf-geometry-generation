using Unity.Collections;
using Unity.Jobs;
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

#if UNITY_EDITOR
        double buildStart = Time.realtimeSinceStartup * 1000.0;
#else
        double buildStart = 0;
#endif
        int rx = _layout.Resolution.x;
        int ry = _layout.Resolution.y;
        int rz = _layout.Resolution.z;
        float cellSize = _layout.CellSize;
        float ox = _layout.Origin.x;
        float oy = _layout.Origin.y;
        float oz = _layout.Origin.z;

        // Precompute strides to avoid multiplication in inner loop
        int rowStride = rx;
        int sliceStride = rx * ry;

        for (int z = 0; z < rz; z++)
        {
            float wz = oz + z * cellSize;
            int zOffset = z * sliceStride;
            for (int y = 0; y < ry; y++)
            {
                float wy = oy + y * cellSize;
                int yOffset = zOffset + y * rowStride;
                for (int x = 0; x < rx; x++)
                {
                    float wx = ox + x * cellSize;
                    int offset = yOffset + x;
                    float val = source.Sample(wx, wy, wz);
                    if (float.IsInfinity(val) || float.IsNaN(val))
                        val = 1f;
                    density[offset] = val;
                    material[offset] = source.GetMaterial(wx, wy, wz);
                }
            }
        }

        buffer.SyncState = BufferSyncState.CpuDirty;

#if UNITY_EDITOR
        double elapsed = (Time.realtimeSinceStartup * 1000.0) - buildStart;
        int totalCells = rx * ry * rz;
        Debug.Log($"[Buffer] Build complete: {totalCells} cells, {elapsed:F0}ms");
#endif
    }

    public void BuildPartial(IVolumeSource source, IVolumeBuffer buffer, BoundsInt region)
    {
        if (source == null || buffer == null)
            return;

        var density = buffer.DensityCpu;
        var material = buffer.MaterialCpu;

#if UNITY_EDITOR
        double partialStart = Time.realtimeSinceStartup * 1000.0;
#endif

        int rx = _layout.Resolution.x;
        int ry = _layout.Resolution.y;
        float cellSize = _layout.CellSize;
        float ox = _layout.Origin.x;
        float oy = _layout.Origin.y;
        float oz = _layout.Origin.z;

        int rowStride = rx;
        int sliceStride = rx * ry;

        // Clamp region bounds once upfront - eliminates per-cell checks
        int pz = Mathf.Max(0, Mathf.Min(_layout.Resolution.z, region.position.z));
        int sz = Mathf.Max(0, Mathf.Min(_layout.Resolution.z, region.position.z + region.size.z)) - pz;
        int py = Mathf.Max(0, Mathf.Min(_layout.Resolution.y, region.position.y));
        int sy = Mathf.Max(0, Mathf.Min(_layout.Resolution.y, region.position.y + region.size.y)) - py;
        int px = Mathf.Max(0, Mathf.Min(rx, region.position.x));
        int sx = Mathf.Max(0, Mathf.Min(rx, region.position.x + region.size.x)) - px;

        for (int k = 0; k < sz; k++)
        {
            int z = pz + k;
            float wz = oz + z * cellSize;
            int zOffset = z * sliceStride;
            for (int j = 0; j < sy; j++)
            {
                int y = py + j;
                float wy = oy + y * cellSize;
                int yOffset = zOffset + y * rowStride;
                for (int i = 0; i < sx; i++)
                {
                    int x = px + i;
                    float wx = ox + x * cellSize;

                    int offset = yOffset + x;
                    float val = source.Sample(wx, wy, wz);
                    if (float.IsInfinity(val) || float.IsNaN(val))
                        val = 1f;
                    density[offset] = val;
                    material[offset] = source.GetMaterial(wx, wy, wz);
                }
            }
        }

        buffer.SyncState = BufferSyncState.CpuDirty;

#if UNITY_EDITOR
        double partialElapsed = (Time.realtimeSinceStartup * 1000.0) - partialStart;
        Debug.Log($"[Buffer] BuildPartial complete: {region.size.x*region.size.y*region.size.z} cells, {partialElapsed:F1}ms");
#endif
    }

    /// <summary>Burst-compiled full SDF sampling — replaces managed Build() for supported shapes.</summary>
    public void BuildBurst(SdfSceneSnapshot snapshot, IVolumeBuffer buffer)
    {
        if (snapshot == null || buffer == null || snapshot.HasUnsupportedShapes)
            return;

#if UNITY_EDITOR
        double burstStart = Time.realtimeSinceStartup * 1000.0;
#endif

        NativeArray<BurstShapeData> shapes = CreateBurstShapes(snapshot, Allocator.TempJob);

        int rx = _layout.Resolution.x;
        int ry = _layout.Resolution.y;
        int rz = _layout.Resolution.z;
        int totalCells = rx * ry * rz;

        var job = new BurstSdfSamplingJob
        {
            Shapes = shapes,
            ShapeCount = snapshot.ShapeCount,
            Rx = rx, Ry = ry, Rz = rz,
            MinX = 0, MaxX = rx - 1, MinY = 0, MaxY = ry - 1, MinZ = 0, MaxZ = rz - 1,
            CellSize = _layout.CellSize,
            WorldMinX = _layout.Origin.x, WorldMinY = _layout.Origin.y, WorldMinZ = _layout.Origin.z,
            Density = buffer.DensityCpu,
            Material = buffer.MaterialCpu,
            OutRowStride = rx,
            OutSliceStride = rx * ry,
        };

        JobHandle handle = job.Schedule(totalCells, 4096);
        handle.Complete();
        shapes.Dispose();

        buffer.SyncState = BufferSyncState.CpuDirty;

#if UNITY_EDITOR
        double burstElapsed = (Time.realtimeSinceStartup * 1000.0) - burstStart;
        Debug.Log($"[Buffer] BuildBurst complete: {totalCells} cells, {burstElapsed:F1}ms");
#endif
    }

    /// <summary>Burst-compiled partial SDF sampling — replaces managed BuildPartial() for supported shapes.</summary>
    public void BuildPartialBurst(SdfSceneSnapshot snapshot, IVolumeBuffer buffer, BoundsInt region)
    {
        if (snapshot == null || buffer == null || snapshot.HasUnsupportedShapes)
            return;

#if UNITY_EDITOR
        double burstPartialStart = Time.realtimeSinceStartup * 1000.0;
#endif

        NativeArray<BurstShapeData> shapes = CreateBurstShapes(snapshot, Allocator.TempJob);

        int rx = _layout.Resolution.x;
        int ry = _layout.Resolution.y;
        int rz = _layout.Resolution.z;

        // Clamp region to grid bounds
        int minX = Mathf.Max(0, Mathf.Min(rx, region.position.x));
        int maxX = Mathf.Max(0, Mathf.Min(rx, region.position.x + region.size.x)) - 1;
        int minY = Mathf.Max(0, Mathf.Min(ry, region.position.y));
        int maxY = Mathf.Max(0, Mathf.Min(ry, region.position.y + region.size.y)) - 1;
        int minZ = Mathf.Max(0, Mathf.Min(rz, region.position.z));
        int maxZ = Mathf.Max(0, Mathf.Min(rz, region.position.z + region.size.z)) - 1;

        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            shapes.Dispose();
            return;
        }

        int sx = maxX - minX + 1;
        int sy = maxY - minY + 1;
        int sz = maxZ - minZ + 1;
        long regionCells = (long)sx * sy * sz;

        var job = new BurstSdfSamplingJob
        {
            Shapes = shapes,
            ShapeCount = snapshot.ShapeCount,
            Rx = rx, Ry = ry, Rz = rz,
            MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, MinZ = minZ, MaxZ = maxZ,
            CellSize = _layout.CellSize,
            WorldMinX = _layout.Origin.x, WorldMinY = _layout.Origin.y, WorldMinZ = _layout.Origin.z,
            Density = buffer.DensityCpu,
            Material = buffer.MaterialCpu,
            OutRowStride = rx,
            OutSliceStride = rx * ry,
        };

        JobHandle handle = job.Schedule((int)regionCells, 4096);
        handle.Complete();
        shapes.Dispose();

        buffer.SyncState = BufferSyncState.CpuDirty;

#if UNITY_EDITOR
        double burstPartialElapsed = (Time.realtimeSinceStartup * 1000.0) - burstPartialStart;
        Debug.Log($"[Buffer] BuildPartialBurst complete: {regionCells} cells, {burstPartialElapsed:F1}ms");
#endif
    }

    private NativeArray<BurstShapeData> CreateBurstShapes(SdfSceneSnapshot snapshot, Allocator allocator)
    {
        NativeArray<BurstShapeData> shapes = new NativeArray<BurstShapeData>(snapshot.ShapeCount, allocator);
        int idx = 0;
        foreach (var s in snapshot.AddShapes)
            shapes[idx++] = new BurstShapeData(s, (int)VolumeOperationRole.Add);
        foreach (var s in snapshot.SubtractShapes)
            shapes[idx++] = new BurstShapeData(s, (int)VolumeOperationRole.Subtract);
        foreach (var s in snapshot.IntersectShapes)
            shapes[idx++] = new BurstShapeData(s, (int)VolumeOperationRole.Intersect);
        return shapes;
    }
}
