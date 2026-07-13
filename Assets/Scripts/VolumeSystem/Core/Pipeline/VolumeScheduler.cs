using System.Collections.Generic;
using UnityEngine;

public class VolumeScheduler
{
    public int MaxChunksPerFrame { get; set; } = 8;
    public float FrameBudgetMs { get; set; } = 5f;
    public bool UseTimeBudget { get; set; } = true;
    public Vector3 CameraPosition { get; set; }

    private DirtyChunkSystem _dirtyChunks;
    private IVolumeMesher _mesher;
    private IVolumeBuffer _buffer;
    private IVolumeOutput _output;
    private VolumeLayout _layout;
    private ChunkRenderManager _chunkRenderers;

    private readonly List<RemeshEntry> _pending = new();

    public bool HasPendingWork => _dirtyChunks.HasPendingWork || _pending.Count > 0;
    public int PendingCount => _dirtyChunks.QueueCount + _pending.Count;

    public VolumeScheduler(DirtyChunkSystem dirtyChunks, IVolumeBuffer buffer, VolumeLayout layout)
    {
        _dirtyChunks = dirtyChunks;
        _buffer = buffer;
        _layout = layout;
    }

    public void SetMesher(IVolumeMesher mesher)
    {
        _mesher = mesher;
    }

    public void SetOutput(IVolumeOutput output)
    {
        _output = output;
    }

    public void SetChunkRenderers(ChunkRenderManager renderers)
    {
        _chunkRenderers = renderers;
    }

    /// <summary>Drain dirty queue into scheduler pending list, sorted by priority.</summary>
    public void CollectPending()
    {
        if (!_dirtyChunks.HasPendingWork) return;

        List<RemeshEntry> entries = _dirtyChunks.DequeueAll();
        foreach (RemeshEntry entry in entries)
        {
            int priority = ComputePriority(entry);
            _pending.Add(new RemeshEntry(entry.Coord, priority, entry.Reason, entry.Version));
        }

        _pending.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>Process up to budget of chunks per frame.</summary>
   public int Tick()
    {
        if (_mesher == null) return 0;

        CollectPending();

       if (_pending.Count == 0) return 0;

        MeshingContext context = MeshingContext.Default(_layout);
        bool chunked = _mesher is IChunkVolumeMesher;

   int processed = 0;
        double startTime = Time.realtimeSinceStartup * 1000.0;

        while (_pending.Count > 0 && processed < MaxChunksPerFrame)
        {
            if (UseTimeBudget && (Time.realtimeSinceStartup * 1000.0 - startTime) > FrameBudgetMs)
                break;

            RemeshEntry entry = _pending[0];

            int currentVersion = _dirtyChunks.GetChunkVersion(entry.Coord);
            if (currentVersion != entry.Version)
            {
                Debug.LogWarning($"[Scheduler] Skipping stale chunk {entry.Coord} (version mismatch)");
                _pending.RemoveAt(0);
                continue;
            }

            CpuMeshData chunkMesh = default;
            if (chunked && _chunkRenderers != null)
            {
                chunkMesh = ((IChunkVolumeMesher)_mesher).BuildChunkCpu(_buffer, entry.Coord, context);
                Debug.Log($"[Scheduler] Chunk {entry.Coord}: {chunkMesh.VertexCount} verts, {chunkMesh.IndexCount} indices");
                _chunkRenderers.Apply(entry.Coord, chunkMesh);
            }
            else if (!chunked)
            {
                break;
            }

            if (chunkMesh.Vertices.IsCreated)
                chunkMesh.Dispose();

            _pending.RemoveAt(0);
            _dirtyChunks.CompleteRemesh(entry.Coord);
            processed++;
        }

        return processed;
    }

    /// <summary>Process all pending chunks immediately (no frame budget).</summary>
    public int TickAll()
    {
        CollectPending();

        if (_pending.Count == 0 || _mesher == null) return 0;

        MeshingContext context = MeshingContext.Default(_layout);
        bool chunked = _mesher is IChunkVolumeMesher;

        int processed = 0;

        if (!chunked || _chunkRenderers == null)
        {
            CpuMeshData meshData = _mesher.BuildCpu(_buffer, context);

            if (meshData.VertexCount > 0 && meshData.IndexCount > 0)
                _output.ApplyCpuMesh(meshData);

            meshData.Dispose();

            foreach (RemeshEntry entry in _pending)
                _dirtyChunks.CompleteRemesh(entry.Coord);

            processed = _pending.Count;
            _pending.Clear();
        }
        else
        {
            while (_pending.Count > 0)
            {
                RemeshEntry entry = _pending[0];

                int currentVersion = _dirtyChunks.GetChunkVersion(entry.Coord);
                if (currentVersion != entry.Version)
                {
                    _pending.RemoveAt(0);
                    continue;
                }

                CpuMeshData chunkMesh = ((IChunkVolumeMesher)_mesher).BuildChunkCpu(_buffer, entry.Coord, context);
                _chunkRenderers.Apply(entry.Coord, chunkMesh);
                if (chunkMesh.Vertices.IsCreated) chunkMesh.Dispose();
                _dirtyChunks.CompleteRemesh(entry.Coord);

                _pending.RemoveAt(0);
                processed++;
            }
        }

        return processed;
    }

    private int ComputePriority(RemeshEntry entry)
    {
        switch (entry.Reason)
        {
            case DirtyReason.Operation: return 100;
            case DirtyReason.FullRebuild: return 50;
            case DirtyReason.MesherSwitch: return 75;
            case DirtyReason.NeighborExpansion: return 25;
            default: return 0;
        }
    }

   public void Clear()
    {
        _pending.Clear();
    }
}
