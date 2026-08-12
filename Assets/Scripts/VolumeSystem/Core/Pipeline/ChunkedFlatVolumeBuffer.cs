using Unity.Collections;
using UnityEngine;

public class ChunkedFlatVolumeBuffer : IVolumeBuffer
{
    private bool _disposed;

    public VolumeLayout Layout { get; private set; }
     public BufferSyncState SyncState { get; set; } = BufferSyncState.Synced;

     public void UpdateOrigin(Vector3 newOrigin)
     {
         var l = Layout;
         l.Origin = newOrigin;
         Layout = l;
     }

    public bool HasCpuAccess => true;
    public bool HasGpuAccess => DensityGpu != null || DensityCompute != null;

    private NativeArray<float> _densityCpu;
    private NativeArray<int> _materialCpu;

    public NativeArray<float> DensityCpu => _densityCpu;
    public NativeArray<int> MaterialCpu => _materialCpu;

    public GraphicsBuffer DensityGpu { get; private set; }
    public GraphicsBuffer MaterialGpu { get; private set; }

    public ComputeBuffer DensityCompute { get; private set; }
    public ComputeBuffer MaterialCompute { get; private set; }

    // Chunk management delegated to ChunkManager
    private IChunkManager _chunkManager;

    public IChunkManager ChunkManager => _chunkManager;

    public ChunkedFlatVolumeBuffer(VolumeLayout layout, Allocator allocator = Allocator.Persistent)
    {
        Layout = layout;
        _densityCpu = new NativeArray<float>(layout.TotalCells, allocator);
        _materialCpu = new NativeArray<int>(layout.TotalCells, allocator);
        SyncState = BufferSyncState.Synced;

        _chunkManager = new ChunkManager(layout);
    }

    public int TotalChunks => _chunkManager.TotalChunks;

    public Vector3Int ChunkGridSize => _chunkManager.ChunkGridSize;

    public VolumeChunk GetChunk(int cx, int cy, int cz) => _chunkManager.GetChunk(cx, cy, cz);

    public int GetChunkIndex(int cx, int cy, int cz) => _chunkManager.GetChunkIndex(cx, cy, cz);

    public Vector3Int GetChunkCoords(Vector3Int cellIndex) => _chunkManager.GetChunkCoords(cellIndex);

    public void Initialize(float defaultDensity = 1f, int defaultMaterial = 0)
    {
        for (int i = 0; i < _densityCpu.Length; i++)
        {
            _densityCpu[i] = defaultDensity;
            _materialCpu[i] = defaultMaterial;
        }
    }

    public void SyncCpuToGpu()
    {
        if (DensityGpu == null && DensityCompute == null) return;

        if (DensityCompute != null)
            DensityCompute.SetData(_densityCpu);

        if (DensityGpu != null)
        {
            float[] densityArr = new float[_densityCpu.Length];
            for (int i = 0; i < _densityCpu.Length; i++) densityArr[i] = _densityCpu[i];
            DensityGpu.SetData(densityArr);
        }

        if (MaterialCompute != null)
            MaterialCompute.SetData(_materialCpu);

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
        if (DensityGpu == null && DensityCompute == null) return;

        if (DensityCompute != null)
            {
                float[] dArr = new float[_densityCpu.Length];
                DensityCompute.GetData(dArr);
                for (int i = 0; i < dArr.Length; i++) _densityCpu[i] = dArr[i];
            }

        if (DensityGpu != null)
        {
            float[] densityArr = new float[_densityCpu.Length];
            DensityGpu.GetData(densityArr);
            for (int i = 0; i < densityArr.Length; i++) _densityCpu[i] = densityArr[i];
        }

        if (MaterialCompute != null)
            {
                int[] mArr = new int[_materialCpu.Length];
                MaterialCompute.GetData(mArr);
                for (int i = 0; i < mArr.Length; i++) _materialCpu[i] = mArr[i];
            }

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

    public void EnableComputeBuffers()
    {
        if (DensityCompute == null)
        {
            DensityCompute = new ComputeBuffer(_densityCpu.Length, sizeof(float));
        }

        if (MaterialCompute == null)
        {
            MaterialCompute = new ComputeBuffer(_materialCpu.Length, sizeof(int));
        }

        SyncCpuToGpu();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_densityCpu.IsCreated) _densityCpu.Dispose();
        if (_materialCpu.IsCreated) _materialCpu.Dispose();

        if (DensityGpu != null) { DensityGpu.Release(); DensityGpu = null; }
        if (MaterialGpu != null) { MaterialGpu.Release(); MaterialGpu = null; }
        if (DensityCompute != null) { DensityCompute.Release(); DensityCompute = null; }
        if (MaterialCompute != null) { MaterialCompute.Release(); MaterialCompute = null; }
    }
}
