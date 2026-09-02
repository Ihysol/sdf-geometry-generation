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
            var cfBuffer = Buffer as ChunkedFlatVolumeBuffer;
            if (cfBuffer != null)
            {
                Bounds worldBounds = new Bounds(
                    _layout.Origin + (Vector3)_layout.Resolution * _layout.CellSize * 0.5f,
                    (Vector3)_layout.Resolution * _layout.CellSize);
                BufferAsEditView view = new BufferAsEditView(cfBuffer);
                EditLayer.ReplayRegion(view, worldBounds, processorTransform);
            }
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

        // Empty region — no overlap with the grid. Nothing in the buffer changed, so this is
        // a no-op, not a full rebuild (ADR-019). Previously this fell back to a full O(n^3)
        // rebuild for a grid that provably didn't change.
        if (dirtyRegion.size.x <= 0 || dirtyRegion.size.y <= 0 || dirtyRegion.size.z <= 0)
        {
            Debug.LogWarning($"[VolumePipeline] Partial: dirty bounds {dirtyBounds} do not intersect the grid — no-op, nothing resampled or remeshed.");
            return;
        }

        // ADR-019: one region policy for both stages. The plan derives the sample region
        // (chunk-expanded remesh range + the active mesher's declared read halo) AND the
        // remesh chunk range from the same dirty region — so sampling and remeshing can no
        // longer drift, which is the root cause of the stale-halo corner-gap bug.
        int readHalo = Mesher?.ReadHaloCells ?? 2;
        PartialRebuildPlan plan = PartialRebuildPlan.Create(dirtyRegion, _layout, Buffer.ChunkGridSize, readHalo);
        BoundsInt sampleRegion = plan.SampleRegion;

        // Burst-compiled partial sampling
        if (TryGetSnapshot(sdfSource, out var burstSnapshot))
        {
            _builder.BuildPartialBurst(burstSnapshot, Buffer, sampleRegion);
        }
        else
        {
            _builder.BuildPartial(Source, Buffer, sampleRegion);
        }

        // 2. Replay persistent edits over the dirty region (ADR-004)
        if (EditLayer.OperationCount > 0 && processorTransform != null)
        {
            var cfBuffer = Buffer as ChunkedFlatVolumeBuffer;
            if (cfBuffer != null)
            {
                BufferAsEditView view = new BufferAsEditView(cfBuffer);
                EditLayer.ReplayRegion(view, dirtyBounds, processorTransform);
            }
        }

        // ADR-019: mark the dirty region for meshing. MarkDirty derives its ±1-chunk remesh
        // range from the same PartialRebuildPlan policy used to size the sample region above,
        // so the two stages cannot drift. (Passing sampleRegion here would double-expand it.)
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
