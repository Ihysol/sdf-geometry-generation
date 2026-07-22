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

        // Compute chunk range without allocations
        int minCx = Mathf.Max(0, region.position.x / chunkSize);
        int minCy = Mathf.Max(0, region.position.y / chunkSize);
        int minCz = Mathf.Max(0, region.position.z / chunkSize);
        int maxCx = Mathf.Min(_gridX - 1, (region.position.x + region.size.x - 1) / chunkSize);
        int maxCy = Mathf.Min(_gridY - 1, (region.position.y + region.size.y - 1) / chunkSize);
        int maxCz = Mathf.Min(_gridZ - 1, (region.position.z + region.size.z - 1) / chunkSize);

        // Expand chunk range by 1 in each direction for neighbor coverage
        int expMinCx = Mathf.Max(0, minCx - 1);
        int expMinCy = Mathf.Max(0, minCy - 1);
        int expMinCz = Mathf.Max(0, minCz - 1);
        int expMaxCx = Mathf.Min(_gridX - 1, maxCx + 1);
        int expMaxCy = Mathf.Min(_gridY - 1, maxCy + 1);
        int expMaxCz = Mathf.Min(_gridZ - 1, maxCz + 1);

        // Mark chunks dirty (expanded range covers affected + neighbors)
        for (int cz = expMinCz; cz <= expMaxCz; cz++)
        {
            for (int cy = expMinCy; cy <= expMaxCy; cy++)
            {
                for (int cx = expMinCx; cx <= expMaxCx; cx++)
                {
                    MarkChunk(cx, cy, cz, reason);
                }
            }
        }
    }

    private void MarkChunk(int x, int y, int z, DirtyReason reason)
    {
        int idx = CoordToIndex(x, y, z);
        if (_states[idx] != DirtyState.MeshingQueued)
        {
            chunkVersion++;
            _states[idx] = DirtyState.MeshingQueued;
            _versions[idx] = chunkVersion;
            _remeshQueue.Add(new RemeshEntry(new ChunkCoord(x, y, z), 0, reason, chunkVersion));
        }
    }

    public void MarkAllDirty(DirtyReason reason = DirtyReason.FullRebuild)
    {
        _remeshQueue.Clear();
        chunkVersion++;

        int total = _gridX * _gridY * _gridZ;
        for (int i = 0; i < total; i++)
        {
            _states[i] = DirtyState.MeshingQueued;
            _versions[i] = chunkVersion;
            // Decode index back to coord
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
