using UnityEngine;

public interface IChunkManager
{
    int TotalChunks { get; }
    Vector3Int ChunkGridSize { get; }
    int ChunkSize { get; }

    VolumeChunk GetChunk(int cx, int cy, int cz);
    bool TryGetChunk(int cx, int cy, int cz, out VolumeChunk chunk);
    int GetChunkIndex(int cx, int cy, int cz);
    Vector3Int GetChunkCoords(Vector3Int cellIndex);

    void IncrementChunkVersion(int cx, int cy, int cz);
}
