using System.Collections.Generic;
using UnityEngine;

public class ChunkManager : IChunkManager
{
    private VolumeLayout _layout;
    private VolumeChunk[] _chunks;
    private Vector3Int _chunkGridSize;

    public int TotalChunks => _chunks != null ? _chunks.Length : 0;
    public Vector3Int ChunkGridSize => _chunkGridSize;
    public int ChunkSize => _layout.ChunkSize;

    public ChunkManager(VolumeLayout layout)
    {
        _layout = layout;
        BuildChunkGrid();
    }

    private void BuildChunkGrid()
    {
        int chunkSize = _layout.ChunkSize;

        if (chunkSize <= 0)
        {
            _chunkGridSize = new Vector3Int(1, 1, 1);
            _chunks = new VolumeChunk[1];
            _chunks[0] = new VolumeChunk
            {
                ChunkIndex = Vector3Int.zero,
                CellBounds = new BoundsInt(0, 0, 0, _layout.Resolution.x, _layout.Resolution.y, _layout.Resolution.z),
                Version = 0
            };
            return;
        }

        _chunkGridSize = new Vector3Int(
            Mathf.CeilToInt((float)_layout.Resolution.x / chunkSize),
            Mathf.CeilToInt((float)_layout.Resolution.y / chunkSize),
            Mathf.CeilToInt((float)_layout.Resolution.z / chunkSize)
        );

        int totalChunks = _chunkGridSize.x * _chunkGridSize.y * _chunkGridSize.z;
        _chunks = new VolumeChunk[totalChunks];

        for (int cz = 0; cz < _chunkGridSize.z; cz++)
        {
            for (int cy = 0; cy < _chunkGridSize.y; cy++)
            {
                for (int cx = 0; cx < _chunkGridSize.x; cx++)
                {
                    Vector3Int chunkIndex = new Vector3Int(cx, cy, cz);

                    int minX = cx * chunkSize;
                    int minY = cy * chunkSize;
                    int minZ = cz * chunkSize;

                    int maxX = Mathf.Min((cx + 1) * chunkSize, _layout.Resolution.x);
                    int maxY = Mathf.Min((cy + 1) * chunkSize, _layout.Resolution.y);
                    int maxZ = Mathf.Min((cz + 1) * chunkSize, _layout.Resolution.z);

                    int index = cx + _chunkGridSize.x * (cy + _chunkGridSize.y * cz);
                    _chunks[index] = new VolumeChunk
                    {
                        ChunkIndex = chunkIndex,
                        CellBounds = new BoundsInt(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ),
                        Version = 0
                    };
                }
            }
        }
    }

    public VolumeChunk GetChunk(int cx, int cy, int cz)
    {
        if (!IsValid(cx, cy, cz)) return default;
        return _chunks[GetFlattenedIndex(cx, cy, cz)];
    }

    public bool TryGetChunk(int cx, int cy, int cz, out VolumeChunk chunk)
    {
        chunk = default;
        if (!IsValid(cx, cy, cz)) return false;
        chunk = _chunks[GetFlattenedIndex(cx, cy, cz)];
        return true;
    }

    public int GetChunkIndex(int cx, int cy, int cz)
    {
        if (!IsValid(cx, cy, cz)) return -1;
        return GetFlattenedIndex(cx, cy, cz);
    }

    public Vector3Int GetChunkCoords(Vector3Int cellIndex)
    {
        int chunkSize = _layout.ChunkSize;
        if (chunkSize <= 0) return Vector3Int.zero;

        return new Vector3Int(
            Mathf.FloorToInt(cellIndex.x / chunkSize),
            Mathf.FloorToInt(cellIndex.y / chunkSize),
            Mathf.FloorToInt(cellIndex.z / chunkSize)
        );
    }

    public void IncrementChunkVersion(int cx, int cy, int cz)
    {
        if (!IsValid(cx, cy, cz)) return;
        int idx = GetFlattenedIndex(cx, cy, cz);
        var chunk = _chunks[idx];
        chunk.Version++;
        _chunks[idx] = chunk;
    }

    public void ResetAllVersions()
    {
        if (_chunks == null) return;
        for (int i = 0; i < _chunks.Length; i++)
        {
            var chunk = _chunks[i];
            chunk.Version = 0;
            _chunks[i] = chunk;
        }
    }

    public bool IsValid(int cx, int cy, int cz)
    {
        return cx >= 0 && cx < _chunkGridSize.x &&
               cy >= 0 && cy < _chunkGridSize.y &&
               cz >= 0 && cz < _chunkGridSize.z;
    }

    private int GetFlattenedIndex(int cx, int cy, int cz)
    {
        return cx + _chunkGridSize.x * (cy + _chunkGridSize.y * cz);
    }
}
