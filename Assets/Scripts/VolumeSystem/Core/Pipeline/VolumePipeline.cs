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

    /// <summary>ADR-004: Persistent edit layer — replayed after SDF sampling, before meshing.</summary>
    public PersistentEditLayer EditLayer { get; private set; }

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
        EditLayer = new PersistentEditLayer(); // ADR-004 Seam 1
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

        // VolumeObjectRegistry exposes TryGetBuiltInSnapshot
        if (sdfSource is VolumeObjectRegistry composer && composer.TryGetBuiltInSnapshot(out var cs))
        {
            snapshot = cs;
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel) => Rebuild(sdfSource, isoLevel, default, null);

    /// <summary>ADR-004: Full rebuild with optional edit replay. Pass processor transform for anchor resolution.</summary>
    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel, Transform processorTransform)
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

        // 1. Sample Authoring Composition into buffer
        if (TryGetSnapshot(sdfSource, out var snapshot))
        {
            _builder.BuildBurst(snapshot, Buffer);
        }
        else
        {
            _builder.Build(Source, Buffer);
        }

        // 2. Replay persistent edits over full volume (ADR-004)
        if (EditLayer.OperationCount > 0 && processorTransform != null)
        {
            Bounds worldBounds = new Bounds(_layout.Origin + (Vector3)_layout.Resolution * _layout.CellSize * 0.5f,
                                             (Vector3)_layout.Resolution * _layout.CellSize);
            BufferAsEditView view = new BufferAsEditView((ChunkedFlatVolumeBuffer)Buffer);
            EditLayer.ReplayRegion(view, worldBounds, processorTransform);
        }

        int cx = _layout.Resolution.x / 2, cy = _layout.Resolution.y / 2, cz = _layout.Resolution.z / 2;
        float centerVal = Buffer.DensityCpu[cx + _layout.Resolution.x * (cy + _layout.Resolution.y * cz)];
        Debug.Log($"[VolumePipeline] Rebuild full: {DirtyChunks.QueueCount} chunks, center={centerVal:F3}, iso={isoLevel}");

        Scheduler.ClearPending();
        DirtyChunks.MarkAllDirty(DirtyReason.FullRebuild);

        if (ActiveBackend == ComputeBackend.GPU)
            Buffer.SyncCpuToGpu();
    }

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel, Bounds dirtyBounds) =>
        Rebuild(sdfSource, isoLevel, dirtyBounds, null);

    /// <summary>ADR-004: Partial rebuild with optional edit replay.</summary>
    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel, Bounds dirtyBounds, Transform processorTransform)
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

        // Expand sampling region by ±1 cell so dual contouring at the boundary
        // reads fresh SDF values (not stale data from before this partial rebuild).
        Vector3Int pos = sampleRegion.position - Vector3Int.one;
        pos.x = Mathf.Max(0, pos.x);
        pos.y = Mathf.Max(0, pos.y);
        pos.z = Mathf.Max(0, pos.z);
        Vector3Int sz = sampleRegion.size + new Vector3Int(2, 2, 2);
        sz.x = Mathf.Min(_layout.Resolution.x - pos.x, sz.x);
        sz.y = Mathf.Min(_layout.Resolution.y - pos.y, sz.y);
        sz.z = Mathf.Min(_layout.Resolution.z - pos.z, sz.z);
        sampleRegion = new BoundsInt(pos, sz);

        // Burst-compiled partial sampling
        if (TryGetSnapshot(sdfSource, out var burstSnapshot))
        {
            _builder.BuildPartialBurst(burstSnapshot, Buffer, sampleRegion);
        }
        else
        {
            _builder.BuildPartial(Source, Buffer, sampleRegion);
        }

        // Mark original dirty region for meshing — MarkDirty() expands ±1 chunk internally.
        // Do NOT pass sampleRegion here (already expanded) — would double-expand neighbor coverage.

        // Replay persistent edits over the dirty region (ADR-004)
        if (EditLayer.OperationCount > 0 && processorTransform != null)
        {
            BufferAsEditView view = new BufferAsEditView((ChunkedFlatVolumeBuffer)Buffer);
            EditLayer.ReplayRegion(view, dirtyBounds, processorTransform);
        }

        DirtyChunks.MarkDirty(dirtyRegion, DirtyReason.Operation);
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

        // Clamp to grid — last chunk index is (res-1)/cs, ensuring cell coords stay in bounds even
        // when resolution is not an exact multiple of chunk size.
        Vector3Int res = layout.Resolution;
        minCx = Mathf.Max(0, minCx); minCy = Mathf.Max(0, minCy); minCz = Mathf.Max(0, minCz);
        maxCx = Mathf.Min((res.x - 1) / cs, maxCx);
        maxCy = Mathf.Min((res.y - 1) / cs, maxCy);
        maxCz = Mathf.Min((res.z - 1) / cs, maxCz);

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
