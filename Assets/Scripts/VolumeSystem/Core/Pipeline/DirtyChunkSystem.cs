using System.Collections.Generic;
using UnityEngine;

public class DirtyChunkSystem
{
    private IVolumeBuffer _buffer;
    // Flat arrays instead of Dictionary for O(1) access without hashing overhead
    private DirtyState[] _states;
    private int[] _versions;
    private int _gridX, _gridY, _gridZ;

    private List<RemeshEntry> _remeshQueue = new();

    public int QueueCount => _remeshQueue.Count;
    public bool HasPendingWork => _remeshQueue.Count > 0;

    /// <summary>ADR-004: Stale entries skipped during last scheduler cycle (for counter-based logging).</summary>
    public int StaleEntriesSkipped { get; private set; }

    /// <summary>Increment the stale counter — called by VolumeScheduler.</summary>
    public void AddStaleEntries(int count) => StaleEntriesSkipped += count;

    public void Initialize(IVolumeBuffer buffer)
    {
        _buffer = buffer;
        var gridSize = buffer.ChunkGridSize;
        _gridX = gridSize.x;
        _gridY = gridSize.y;
        _gridZ = gridSize.z;

        int total = _gridX * _gridY * _gridZ;
        _states = new DirtyState[total];
        _versions = new int[total];
    }

    private int CoordToIndex(int x, int y, int z) => x + _gridX * (y + _gridY * z);

    public void MarkDirty(BoundsInt region, DirtyReason reason = DirtyReason.Operation)
    {
        int chunkSize = _buffer.Layout.ChunkSize;
        if (chunkSize <= 0)
        {
            MarkChunk(0, 0, 0, reason);
            return;
        }

        // ADR-019: derive the remesh range from the shared region policy so it cannot drift
        // from the sampling side (VolumePipeline builds the same range via
        // PartialRebuildPlan.Create). Same ±1-chunk neighbour coverage as before.
        Vector3Int chunkGrid = new Vector3Int(_gridX, _gridY, _gridZ);
        (Vector3Int min, Vector3Int max) = PartialRebuildPlan.RemeshRange(region, chunkGrid, chunkSize);
        MarkDirtyRange(min, max, reason);
    }

    /// <summary>
    /// ADR-019: Mark an explicit chunk range dirty, with no further expansion. Used by
    /// <c>VolumePipeline</c> which already computed the range via <see cref="PartialRebuildPlan"/>,
    /// so the remesh range and the sampling region are guaranteed consistent.
    /// </summary>
    public void MarkDirtyRange(Vector3Int min, Vector3Int max, DirtyReason reason = DirtyReason.Operation)
    {
        for (int cz = min.z; cz <= max.z; cz++)
        {
            for (int cy = min.y; cy <= max.y; cy++)
            {
                for (int cx = min.x; cx <= max.x; cx++)
                {
                    MarkChunk(cx, cy, cz, reason);
                }
            }
        }
    }

    private void MarkChunk(int x, int y, int z, DirtyReason reason)
    {
        int idx = CoordToIndex(x, y, z);
        chunkVersion++;

        // ADR-004 fix: Always add a fresh queue entry. Just bumping the version leaves
        // the old pending entry orphaned — it gets stale-skipped and the chunk is never remeshed.
        _states[idx] = DirtyState.MeshingQueued;
        _versions[idx] = chunkVersion;
        _remeshQueue.Add(new RemeshEntry(new ChunkCoord(x, y, z), 0, reason, chunkVersion));
    }

    public void MarkAllDirty(DirtyReason reason = DirtyReason.FullRebuild)
    {
        _remeshQueue.Clear();
        StaleEntriesSkipped = 0;

        int total = _gridX * _gridY * _gridZ;
        for (int i = 0; i < total; i++)
        {
            chunkVersion++; // Unique version per chunk — prevents stale check collision if re-dirtied during tick.
            _states[i] = DirtyState.MeshingQueued;
            _versions[i] = chunkVersion;
            int temp = i;
            int z = temp / (_gridX * _gridY);
            temp %= (_gridX * _gridY);
            int y = temp / _gridX;
            int x = temp % _gridX;
            _remeshQueue.Add(new RemeshEntry(new ChunkCoord(x, y, z), 0, reason, chunkVersion));
        }
    }

    public List<RemeshEntry> DequeueAll()
    {
        // Swap with empty list - no allocation
        var result = _remeshQueue;
        _remeshQueue = new List<RemeshEntry>(result.Capacity);
        return result;
    }

    public void CompleteRemesh(ChunkCoord coord)
    {
        int idx = CoordToIndex(coord.X, coord.Y, coord.Z);
        _states[idx] = DirtyState.MeshReady;
    }

    public void ClearAllDirty()
    {
        _remeshQueue.Clear();
        System.Array.Fill(_states, DirtyState.Clean);
        StaleEntriesSkipped = 0;
    }

    public int GetChunkVersion(ChunkCoord coord)
    {
        return _versions[CoordToIndex(coord.X, coord.Y, coord.Z)];
    }

    public DirtyState GetChunkDirtyState(ChunkCoord coord)
    {
        return _states[CoordToIndex(coord.X, coord.Y, coord.Z)];
    }

    private int chunkVersion = 0;
}
