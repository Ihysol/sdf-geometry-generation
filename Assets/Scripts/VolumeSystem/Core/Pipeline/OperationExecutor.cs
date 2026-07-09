using UnityEngine;

public struct VolumeOperationContext
{
    public bool StoreUndo;
    public bool DirectApply;
    public bool ReportDirtyBounds;
    public bool ScheduleRemesh;
    public float DeltaTime;

    public static VolumeOperationContext DefaultPersistent()
    {
        return new VolumeOperationContext
        {
            StoreUndo = true,
            DirectApply = false,
            ReportDirtyBounds = true,
            ScheduleRemesh = true,
            DeltaTime = Time.deltaTime
        };
    }

    public static VolumeOperationContext DefaultDirect()
    {
        return new VolumeOperationContext
        {
            StoreUndo = false,
            DirectApply = true,
            ReportDirtyBounds = true,
            ScheduleRemesh = true,
            DeltaTime = Time.deltaTime
        };
    }
}

public class OperationExecutor
{
    private DirtyChunkSystem _dirtyChunks;
    private ComputeBackend _backend;

    public OperationExecutor(DirtyChunkSystem dirtyChunks, ComputeBackend backend)
    {
        _dirtyChunks = dirtyChunks;
        _backend = backend;
    }

    public void SetBackend(ComputeBackend backend)
    {
        _backend = backend;
    }

    /// <summary>Executes an operation and reports dirty bounds.</summary>
    public void Execute(IVolumeOperation operation, IVolumeBuffer buffer, VolumeOperationContext context)
    {
        if (operation == null || buffer == null) return;

        ApplyOperation(operation, buffer);

        if (context.ReportDirtyBounds && _dirtyChunks != null)
        {
            _dirtyChunks.MarkDirty(operation.AffectedRegion, DirtyReason.Operation);
        }
    }

    /// <summary>Executes multiple operations as a single transaction.</summary>
    public void ExecuteTransaction(IVolumeOperation[] operations, IVolumeBuffer buffer, VolumeOperationContext context)
    {
        if (operations == null || buffer == null) return;

        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        bool first = true;

        foreach (var op in operations)
        {
            ApplyOperation(op, buffer);

            var r = op.AffectedRegion;
            Vector3Int rMax = r.position + r.size;

            if (first)
            {
                min = r.position;
                max = rMax;
                first = false;
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    min[i] = Mathf.Min(min[i], r.position[i]);
                    max[i] = Mathf.Max(max[i], rMax[i]);
                }
            }
        }

        if (!first && context.ReportDirtyBounds && _dirtyChunks != null)
        {
            BoundsInt combinedRegion = new BoundsInt(min, max - min);
            _dirtyChunks.MarkDirty(combinedRegion, DirtyReason.Operation);
        }
    }

    private void ApplyOperation(IVolumeOperation operation, IVolumeBuffer buffer)
    {
        if (_backend == ComputeBackend.GPU && operation.SupportsGpu)
        {
            operation.ApplyGpu(buffer, null);
            if (buffer.SyncState != BufferSyncState.Synced)
                buffer.SyncGpuToCpu();
        }
        else if (operation.SupportsCpu)
        {
            operation.ApplyCpu(buffer);
        }
    }
}
