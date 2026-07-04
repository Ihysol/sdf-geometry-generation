using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class FlatGridVolumeBuffer : IVolumeBuffer
{
    private bool _disposed;

    public VolumeLayout Layout { get; private set; }
    public BufferSyncState SyncState { get; private set; } = BufferSyncState.Synced;

    public bool HasCpuAccess => true;
    public bool HasGpuAccess => DensityGpu != null;

    private NativeArray<float> _densityCpu;
    private NativeArray<int> _materialCpu;

    public NativeArray<float> DensityCpu => _densityCpu;
    public NativeArray<int> MaterialCpu => _materialCpu;

    public GraphicsBuffer DensityGpu { get; private set; }
    public GraphicsBuffer MaterialGpu { get; private set; }

    private readonly List<BoundsInt> _dirtyRegions = new();

    public FlatGridVolumeBuffer(VolumeLayout layout, Allocator allocator = Allocator.Persistent)
    {
        Layout = layout;
        _densityCpu = new NativeArray<float>(layout.TotalCells, allocator);
        _materialCpu = new NativeArray<int>(layout.TotalCells, allocator);
        SyncState = BufferSyncState.Synced;
    }

    public void Initialize(float defaultDensity = 1f, int defaultMaterial = 0)
    {
        for (int i = 0; i < _densityCpu.Length; i++)
        {
            _densityCpu[i] = defaultDensity;
            _materialCpu[i] = defaultMaterial;
        }
    }

    public void SampleSource(IVolumeSource source, float isoLevel = 0f)
    {
        for (int z = 0; z < Layout.Resolution.z; z++)
        {
            for (int y = 0; y < Layout.Resolution.y; y++)
            {
                for (int x = 0; x < Layout.Resolution.x; x++)
                {
                    Vector3Int index = new Vector3Int(x, y, z);
                    Vector3 world = Layout.IndexToWorld(index);
                    float value = source.Sample(world);

                    int offset = Layout.IndexToOffset(index);
                    _densityCpu[offset] = value - isoLevel;
                    _materialCpu[offset] = source.GetMaterial(world);
                }
            }
        }

        SyncState = BufferSyncState.CpuDirty;
    }

    public void MarkDirty(BoundsInt region)
    {
        _dirtyRegions.Add(region);
    }

    public IReadOnlyList<BoundsInt> GetDirtyRegions()
    {
        return _dirtyRegions;
    }

    public void ClearDirtyRegions()
    {
        _dirtyRegions.Clear();
    }

    public void SyncCpuToGpu()
    {
        if (DensityGpu == null) return;

        float[] densityArr = new float[_densityCpu.Length];
        for (int i = 0; i < _densityCpu.Length; i++) densityArr[i] = _densityCpu[i];
        DensityGpu.SetData(densityArr);

        if (MaterialGpu != null)
        {
            int[] materialArr = new int[_materialCpu.Length];
            for (int i = 0; i < _materialCpu.Length; i++) materialArr[i] = _materialCpu[i];
            MaterialGpu.SetData(materialArr);
        }

        SyncState = BufferSyncState.Synced;
    }

    public void SyncGpuToCpu()
    {
        if (DensityGpu == null) return;

        float[] densityArr = new float[_densityCpu.Length];
        DensityGpu.GetData(densityArr);
        for (int i = 0; i < densityArr.Length; i++) _densityCpu[i] = densityArr[i];

        if (MaterialGpu != null)
        {
            int[] materialArr = new int[_materialCpu.Length];
            MaterialGpu.GetData(materialArr);
            for (int i = 0; i < materialArr.Length; i++) _materialCpu[i] = materialArr[i];
        }

        SyncState = BufferSyncState.Synced;
    }

    public void EnableGpuBuffers()
    {
        if (DensityGpu == null)
        {
            DensityGpu = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sizeof(float), _densityCpu.Length);
            SyncCpuToGpu();
        }

        if (MaterialGpu == null)
        {
            MaterialGpu = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sizeof(int), _materialCpu.Length);
            SyncCpuToGpu();
        }

        SyncState = BufferSyncState.Synced;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_densityCpu.IsCreated) _densityCpu.Dispose();
        if (_materialCpu.IsCreated) _materialCpu.Dispose();

        if (DensityGpu != null) { DensityGpu.Release(); DensityGpu = null; }
        if (MaterialGpu != null) { MaterialGpu.Release(); MaterialGpu = null; }
    }
}
