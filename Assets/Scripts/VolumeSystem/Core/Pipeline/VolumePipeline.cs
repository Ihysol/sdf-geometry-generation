using System.Collections.Generic;
using UnityEngine;

public class VolumePipeline
{
    public IVolumeSource Source { get; set; }
    public IVolumeBuffer Buffer { get; private set; }
    public IVolumeMesher Mesher { get; set; }
    public IVolumeOutput Output { get; set; }
    public DirtyChunkSystem DirtyChunks { get; private set; }
    public OperationExecutor Executor { get; private set; }
    public VolumeScheduler Scheduler { get; private set; }

    private InitialBufferBuilder _builder;
    private VolumeLayout _layout;
    public ComputeBackend ActiveBackend { get; private set; } = ComputeBackend.CPU;

    private bool _dirty = true;

    public VolumePipeline(VolumeLayout layout, IVolumeMesher mesher)
    {
        _layout = layout;
        Mesher = mesher;
    }

    public void Initialize(IVolumeOutput output)
    {
        Output = output;
        Buffer = new ChunkedFlatVolumeBuffer(_layout);
        _builder = new InitialBufferBuilder(_layout);
        DirtyChunks = new DirtyChunkSystem();
        DirtyChunks.Initialize(Buffer);
        Executor = new OperationExecutor(DirtyChunks, ActiveBackend);
        Scheduler = new VolumeScheduler(DirtyChunks, Buffer, _layout);
        Scheduler.SetMesher(Mesher);
        Scheduler.SetOutput(Output);
    }

    public void SetBackend(ComputeBackend backend)
    {
        ActiveBackend = backend;

        if (Buffer == null) return;

        switch (backend)
        {
            case ComputeBackend.GPU:
                Buffer.EnableComputeBuffers();
                break;
        }

        Executor?.SetBackend(backend);
    }

    public void SetChunkRenderers(ChunkRenderManager renderers)
    {
        Scheduler?.SetChunkRenderers(renderers);
    }

    /// <summary>Extract SdfSceneSnapshot from source if available (Burst path), otherwise wrap as adapter.</summary>
    private bool TryGetSnapshot(IScalarFieldSource sdfSource, out SdfSceneSnapshot snapshot)
    {
        // Direct snapshot
        if (sdfSource is SdfSceneSnapshot direct)
        {
            snapshot = direct;
            return !snapshot.HasUnsupportedShapes;
        }

        // VolumeSceneComposer exposes TryGetBuiltInSnapshot
        if (sdfSource is VolumeSceneComposer composer && composer.TryGetBuiltInSnapshot(out var cs))
        {
            snapshot = cs;
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel)
    {
        if (sdfSource != null)
            Source = new SdfSourceAdapter(sdfSource);

        if (Source == null || Buffer == null || Mesher == null)
        {
            Debug.LogError("[VolumePipeline] Rebuild failed: missing Source/Buffer/Mesher");
            return;
        }

        _layout.IsoLevel = isoLevel;
        _dirty = false;

        // Burst-compiled sampling when snapshot has only supported shapes
        if (TryGetSnapshot(sdfSource, out var snapshot))
        {
            _builder.BuildBurst(snapshot, Buffer);
        }
        else
        {
            _builder.Build(Source, Buffer);
        }

        int cx = _layout.Resolution.x / 2, cy = _layout.Resolution.y / 2, cz = _layout.Resolution.z / 2;
        float centerVal = Buffer.DensityCpu[cx + _layout.Resolution.x * (cy + _layout.Resolution.y * cz)];
        Debug.Log($"[VolumePipeline] Rebuild full: {DirtyChunks.QueueCount} chunks, center={centerVal:F3}, iso={isoLevel}");

        Scheduler.ClearPending();
        DirtyChunks.MarkAllDirty(DirtyReason.FullRebuild);

        if (ActiveBackend == ComputeBackend.GPU)
            Buffer.SyncCpuToGpu();
    }

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel, Bounds dirtyBounds)
    {
        if (sdfSource != null)
            Source = new SdfSourceAdapter(sdfSource);

        if (Source == null || Buffer == null || Mesher == null)
        {
            Debug.LogError("[VolumePipeline] Rebuild partial failed: missing Source/Buffer/Mesher");
            return;
        }

        _layout.IsoLevel = isoLevel;
        _dirty = false;

        BoundsInt dirtyRegion = WorldBoundsToIntBounds(dirtyBounds, _layout);

        // Empty region — fallback to full rebuild
        if (dirtyRegion.size.x <= 0 || dirtyRegion.size.y <= 0 || dirtyRegion.size.z <= 0)
        {
            Debug.Log("[VolumePipeline] Partial: no overlap — fallback to full rebuild");

            if (TryGetSnapshot(sdfSource, out var snapshot))
                _builder.BuildBurst(snapshot, Buffer);
            else
                _builder.Build(Source, Buffer);

            Scheduler.ClearPending();
            DirtyChunks.MarkAllDirty(DirtyReason.FullRebuild);
            if (ActiveBackend == ComputeBackend.GPU)
                Buffer.SyncCpuToGpu();
            return;
        }

        BoundsInt sampleRegion = ExpandToChunkRegions(dirtyRegion, _layout);

        // Burst-compiled partial sampling
        if (TryGetSnapshot(sdfSource, out var burstSnapshot))
        {
            _builder.BuildPartialBurst(burstSnapshot, Buffer, sampleRegion);
        }
        else
        {
            _builder.BuildPartial(Source, Buffer, sampleRegion);
        }

        DirtyChunks.MarkDirty(sampleRegion, DirtyReason.Operation);
        Debug.Log($"[VolumePipeline] Partial: dirty={dirtyRegion}, sample={sampleRegion}, chunks={DirtyChunks.QueueCount}");

        if (ActiveBackend == ComputeBackend.GPU)
            Buffer.SyncCpuToGpu();
    }

    public void RebuildGpu()
    {
        if (Source == null || Buffer == null || Mesher == null || Output == null)
            return;

        _builder.Build(Source, Buffer);
        Scheduler.ClearPending();
        DirtyChunks.MarkAllDirty(DirtyReason.FullRebuild);
        Buffer.SyncCpuToGpu();

        MeshingContext context = MeshingContext.Default(_layout);
        if (!Mesher.SupportsGpu) return;

        GpuMeshData meshData = Mesher.BuildGpu(Buffer, context);

        if (meshData.VertexCount > 0 && meshData.IndexCount > 0)
            Output.ApplyGpuMesh(meshData);
        else
            meshData.Dispose();
    }

    public int TickScheduler()
    {
        if (Scheduler == null) return 0;
        int processed = Scheduler.Tick();
        if (!Scheduler.HasPendingWork && !DirtyChunks.HasPendingWork)
            _dirty = false;
        return processed;
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

    /// <summary>Expand a region to cover all cells in affected chunks plus their 6-face neighbors.</summary>
    private static BoundsInt ExpandToChunkRegions(BoundsInt region, VolumeLayout layout)
    {
        int cs = layout.ChunkSize;
        if (cs <= 0) return region;

        // Find chunk range
        int minCx = Mathf.FloorToInt(region.position.x / cs);
        int minCy = Mathf.FloorToInt(region.position.y / cs);
        int minCz = Mathf.FloorToInt(region.position.z / cs);
        int maxCx = Mathf.FloorToInt((region.position.x + region.size.x - 1) / cs);
        int maxCy = Mathf.FloorToInt((region.position.y + region.size.y - 1) / cs);
        int maxCz = Mathf.FloorToInt((region.position.z + region.size.z - 1) / cs);

        // Add neighbor chunks for dual contouring context
        minCx--; minCy--; minCz--;
        maxCx++; maxCy++; maxCz++;

        // Clamp to grid
        Vector3Int res = layout.Resolution;
        minCx = Mathf.Max(0, minCx); minCy = Mathf.Max(0, minCy); minCz = Mathf.Max(0, minCz);
        int chunkGridMax = cs * Mathf.CeilToInt((float)res.x / cs) - 1;
        maxCx = Mathf.Min(chunkGridMax, maxCx);
        maxCy = Mathf.Min(cs * Mathf.CeilToInt((float)res.y / cs) - 1, maxCy);
        maxCz = Mathf.Min(cs * Mathf.CeilToInt((float)res.z / cs) - 1, maxCz);

        // Convert back to cell indices
        int px = minCx * cs;
        int py = minCy * cs;
        int pz = minCz * cs;
        int sx = Mathf.Min(res.x, (maxCx + 1) * cs) - px;
        int sy = Mathf.Min(res.y, (maxCy + 1) * cs) - py;
        int sz = Mathf.Min(res.z, (maxCz + 1) * cs) - pz;

        return new BoundsInt(px, py, pz, sx, sy, sz);
    }

    public void ApplyOperation(IVolumeOperation operation)
    {
        if (Buffer == null || Executor == null) return;

        VolumeOperationContext ctx = VolumeOperationContext.DefaultDirect();
        Executor.Execute(operation, Buffer, ctx);
    }

    public void MarkDirty() => _dirty = true;
    public bool IsDirty => _dirty;

    public void Clear()
    {
        Output?.Clear();
        _dirty = false;
    }

    public void Dispose()
    {
        Scheduler?.Clear();
        if (Buffer != null)
        {
            Buffer.Dispose();
            Buffer = null;
        }
    }
}

