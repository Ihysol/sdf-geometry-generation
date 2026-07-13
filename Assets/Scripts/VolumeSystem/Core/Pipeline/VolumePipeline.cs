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

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel)
    {
        if (Source == null && sdfSource != null)
            Source = new SdfSourceAdapter(sdfSource);

        if (Source == null || Buffer == null || Mesher == null || Output == null)
            return;

        _layout.IsoLevel = isoLevel;
        _dirty = false;
        _builder.Build(Source, Buffer);
        DirtyChunks.MarkAllDirty(DirtyReason.FullRebuild);

        if (ActiveBackend == ComputeBackend.GPU)
            Buffer.SyncCpuToGpu();
    }

    public void Rebuild(IScalarFieldSource sdfSource, float isoLevel, Bounds dirtyBounds)
    {
        if (Source == null && sdfSource != null)
            Source = new SdfSourceAdapter(sdfSource);

        if (Source == null || Buffer == null || Mesher == null || Output == null)
            return;

        _layout.IsoLevel = isoLevel;
        _dirty = false;

        BoundsInt dirtyRegion = WorldBoundsToIntBounds(dirtyBounds, _layout);
        _builder.BuildPartial(Source, Buffer, dirtyRegion);
        DirtyChunks.MarkDirty(dirtyRegion, DirtyReason.Operation);

        if (ActiveBackend == ComputeBackend.GPU)
            Buffer.SyncCpuToGpu();
    }

    public void RebuildGpu()
    {
        if (Source == null || Buffer == null || Mesher == null || Output == null)
            return;

        _builder.Build(Source, Buffer);
        DirtyChunks.MarkAllDirty(DirtyReason.FullRebuild);
        Buffer.SyncCpuToGpu();

        MeshingContext context = MeshingContext.Default(_layout);
        if (!Mesher.SupportsGpu) return;

        GpuMeshData meshData = Mesher.BuildGpu(Buffer, context);

        if (meshData.VertexCount > 0 && meshData.IndexCount > 0)
        {
            Output.ApplyGpuMesh(meshData);
        }
        else
        {
            meshData.Dispose();
        }
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
        Scheduler?.Clear();
        if (Buffer != null)
        {
            Buffer.Dispose();
            Buffer = null;
        }
    }
}
