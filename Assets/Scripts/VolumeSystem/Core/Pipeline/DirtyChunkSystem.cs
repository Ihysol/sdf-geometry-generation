using System.Collections.Generic;
using UnityEngine;

public class DirtyChunkSystem
{
    private IVolumeBuffer _buffer;
    private Dictionary<ChunkCoord, ChunkMetadata> _chunkMeta = new();
    private List<RemeshEntry> _remeshQueue = new();

    public int QueueCount => _remeshQueue.Count;
    public bool HasPendingWork => _remeshQueue.Count > 0;

    public void Initialize(IVolumeBuffer buffer)
    {
        _buffer = buffer;
        _chunkMeta.Clear();

        var gridSize = buffer.ChunkGridSize;

        for (int cz = 0; cz < gridSize.z; cz++)
        {
            for (int cy = 0; cy < gridSize.y; cy++)
            {
                for (int cx = 0; cx < gridSize.x; cx++)
                {
                    ChunkCoord coord = new ChunkCoord(cx, cy, cz);
                    _chunkMeta[coord] = new ChunkMetadata
                    {
                        Coord = coord,
                        State = DirtyState.Clean,
                        Version = 0
                    };
                }
            }
        }
    }

 public void MarkDirty(BoundsInt region, DirtyReason reason = DirtyReason.Operation)
    {
        HashSet<ChunkCoord> affected = GetChunksOverlappingRegion(region);
        ExpandNeighbors(affected);

        foreach (ChunkCoord coord in affected)
        {
            if (_chunkMeta.TryGetValue(coord, out ChunkMetadata meta))
            {
                if (meta.State != DirtyState.MeshingQueued)
                {
                    chunkVersion++;
                    meta.State = DirtyState.MeshingQueued;
                    meta.Version = chunkVersion;
                    _chunkMeta[coord] = meta;

                    _remeshQueue.Add(new RemeshEntry(coord, 0, reason, chunkVersion));
                }
            }
        }
    }

    public void MarkAllDirty(DirtyReason reason = DirtyReason.FullRebuild)
    {
        _remeshQueue.Clear();
        chunkVersion++;

        var keys = new List<ChunkCoord>(_chunkMeta.Keys);
        foreach (var coord in keys)
        {
            ChunkMetadata meta = _chunkMeta[coord];
            meta.State = DirtyState.MeshingQueued;
            meta.Version = chunkVersion;
            _chunkMeta[coord] = meta;

            _remeshQueue.Add(new RemeshEntry(coord, 0, reason, chunkVersion));
        }
    }

    public List<RemeshEntry> DequeueAll()
    {
        List<RemeshEntry> result = new List<RemeshEntry>(_remeshQueue);
        _remeshQueue.Clear();

        foreach (RemeshEntry entry in result)
        {
            if (_chunkMeta.TryGetValue(entry.Coord, out ChunkMetadata meta))
            {
                meta.State = DirtyState.MeshingQueued;
                _chunkMeta[entry.Coord] = meta;
            }
        }

        return result;
    }

    public void CompleteRemesh(ChunkCoord coord)
    {
        if (_chunkMeta.TryGetValue(coord, out ChunkMetadata meta))
        {
            meta.State = DirtyState.MeshReady;
            _chunkMeta[coord] = meta;
        }
    }

    public void ClearAllDirty()
    {
        _remeshQueue.Clear();

        foreach (var kvp in _chunkMeta)
        {
            ChunkMetadata meta = kvp.Value;
            meta.State = DirtyState.Clean;
            _chunkMeta[kvp.Key] = meta;
        }
    }

    public int GetChunkVersion(ChunkCoord coord)
    {
        if (_chunkMeta.TryGetValue(coord, out ChunkMetadata meta))
            return meta.Version;
        return 0;
    }

    public DirtyState GetChunkDirtyState(ChunkCoord coord)
    {
        if (_chunkMeta.TryGetValue(coord, out ChunkMetadata meta))
            return meta.State;
        return DirtyState.Clean;
    }

    private HashSet<ChunkCoord> GetChunksOverlappingRegion(BoundsInt region)
    {
        HashSet<ChunkCoord> result = new HashSet<ChunkCoord>();
        int chunkSize = _buffer.Layout.ChunkSize;

        if (chunkSize <= 0)
        {
            result.Add(new ChunkCoord(0, 0, 0));
            return result;
        }

        var gridSize = _buffer.ChunkGridSize;

        int minCx = Mathf.FloorToInt(region.position.x / chunkSize);
        int minCy = Mathf.FloorToInt(region.position.y / chunkSize);
        int minCz = Mathf.FloorToInt(region.position.z / chunkSize);

        int maxCx = Mathf.FloorToInt((region.position.x + region.size.x - 1) / chunkSize);
        int maxCy = Mathf.FloorToInt((region.position.y + region.size.y - 1) / chunkSize);
        int maxCz = Mathf.FloorToInt((region.position.z + region.size.z - 1) / chunkSize);

        minCx = Mathf.Max(0, minCx);
        minCy = Mathf.Max(0, minCy);
        minCz = Mathf.Max(0, minCz);
        maxCx = Mathf.Min(gridSize.x - 1, maxCx);
        maxCy = Mathf.Min(gridSize.y - 1, maxCy);
        maxCz = Mathf.Min(gridSize.z - 1, maxCz);

        for (int cz = minCz; cz <= maxCz; cz++)
        {
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    result.Add(new ChunkCoord(cx, cy, cz));
                }
            }
        }

        return result;
    }

    private void ExpandNeighbors(HashSet<ChunkCoord> affected)
    {
        HashSet<ChunkCoord> expanded = new HashSet<ChunkCoord>(affected);
        var gridSize = _buffer.ChunkGridSize;

        foreach (ChunkCoord coord in affected)
        {
            // 6-face neighbors
            int[] deltas = { -1, 1 };
            foreach (int dx in deltas)
            {
                int nx = coord.X + dx;
                if (nx >= 0 && nx < gridSize.x)
                    expanded.Add(new ChunkCoord(nx, coord.Y, coord.Z));
            }
            foreach (int dy in deltas)
            {
                int ny = coord.Y + dy;
                if (ny >= 0 && ny < gridSize.y)
                    expanded.Add(new ChunkCoord(coord.X, ny, coord.Z));
            }
            foreach (int dz in deltas)
            {
                int nz = coord.Z + dz;
                if (nz >= 0 && nz < gridSize.z)
                    expanded.Add(new ChunkCoord(coord.X, coord.Y, nz));
            }
        }

        affected.Clear();
        foreach (ChunkCoord coord in expanded)
        {
            affected.Add(coord);
        }
    }

    private int chunkVersion = 0;

    private struct ChunkMetadata
    {
        public ChunkCoord Coord;
        public DirtyState State;
        public int Version;
    }
}
