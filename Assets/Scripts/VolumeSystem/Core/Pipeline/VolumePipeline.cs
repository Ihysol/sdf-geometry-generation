using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class VolumePipeline
{
    public IVolumeSource Source { get; set; }
    public IVolumeBuffer Buffer { get; private set; }
    public IVolumeMesher Mesher { get; set; }
    public IVolumeOutput Output { get; set; }

    private VolumeLayout _layout;
    public VolumeLayout Layout => _layout;
    public ComputeBackend Backend => ComputeBackend.CPU;

    private bool _dirty = true;

    public VolumePipeline(VolumeLayout layout, IVolumeMesher mesher)
    {
        _layout = layout;
        Mesher = mesher;
    }

    public void Initialize(IVolumeOutput output)
    {
        Output = output;
        Buffer = new FlatGridVolumeBuffer(_layout);
    }

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel)
    {
        if (Source == null && sdfSource != null)
            Source = new SdfSourceAdapter(sdfSource);

        if (Source == null || Buffer == null || Mesher == null || Output == null)
            return;

        _layout.IsoLevel = isoLevel;

        if (Buffer is FlatGridVolumeBuffer flatBuffer)
            flatBuffer.SampleSource(Source, isoLevel);

        MeshingContext context = MeshingContext.Default(_layout);
        CpuMeshData meshData = Mesher.BuildCpu(Buffer, context);

        if (meshData.VertexCount > 0 && meshData.IndexCount > 0)
        {
            Output.ApplyCpuMesh(meshData);
        }

        if (meshData.Vertices.IsCreated) meshData.Dispose();
    }

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel, Bounds dirtyBounds)
    {
        if (Source == null && sdfSource != null)
            Source = new SdfSourceAdapter(sdfSource);

        if (Source == null || Buffer == null || Mesher == null || Output == null)
            return;

        _layout.IsoLevel = isoLevel;

        BoundsInt dirtyRegion = WorldBoundsToIntBounds(dirtyBounds, _layout);
        SampleDirtyRegion(Source, dirtyRegion, isoLevel);
        Buffer.MarkDirty(dirtyRegion);

        MeshingContext context = MeshingContext.Default(_layout);
        CpuMeshData meshData = Mesher.BuildCpu(Buffer, context);

        if (meshData.VertexCount > 0 && meshData.IndexCount > 0)
        {
            Output.ApplyCpuMesh(meshData);
        }

        if (meshData.Vertices.IsCreated) meshData.Dispose();
    }

    private void SampleDirtyRegion(IVolumeSource source, BoundsInt region, float isoLevel)
    {
        NativeArray<float> density = Buffer.DensityCpu;
        NativeArray<int> material = Buffer.MaterialCpu;

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

                    density[offset] = source.Sample(world) - isoLevel;
                    material[offset] = source.GetMaterial(world);
                }
            }
        }
    }

    private static BoundsInt WorldBoundsToIntBounds(Bounds worldBounds, VolumeLayout layout)
    {
        Vector3Int minIndex = layout.WorldToIndex(worldBounds.min);
        Vector3Int maxIndex = layout.WorldToIndex(worldBounds.max);

        int px = Mathf.Max(0, minIndex.x);
        int py = Mathf.Max(0, minIndex.y);
        int pz = Mathf.Max(0, minIndex.z);
        int sx = Mathf.Min(layout.Resolution.x, maxIndex.x + 1) - px;
        int sy = Mathf.Min(layout.Resolution.y, maxIndex.y + 1) - py;
        int sz = Mathf.Min(layout.Resolution.z, maxIndex.z + 1) - pz;

        return new BoundsInt(px, py, pz, sx, sy, sz);
    }

    public void ApplyOperation(IVolumeOperation operation)
    {
        if (Buffer == null) return;

        if (operation.SupportsCpu)
        {
            operation.ApplyCpu(Buffer);
        }

        Buffer.MarkDirty(operation.AffectedRegion);
        _dirty = true;
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    public bool IsDirty => _dirty;

    public void Clear()
    {
        if (Output != null)
            Output.Clear();
        _dirty = false;
    }

    public void Dispose()
    {
        if (Buffer != null)
        {
            Buffer.Dispose();
            Buffer = null;
        }
    }
}
