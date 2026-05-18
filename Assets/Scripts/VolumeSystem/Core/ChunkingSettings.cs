using UnityEngine;

[System.Serializable]
public struct ChunkingSettings
{
    public Vector3Int voxelChunkCount;
    public int octreeTargetTrianglesPerChunk;
    public int octreeEstimatedTrianglesPerLeaf;
    public int octreeMaxLeafNodesPerChunk;
}
