using System.Collections.Generic;
using UnityEngine;

public class SDFChunk
{
    public Vector3Int coord;
    public Vector3 localOrigin;
    public Vector3Int cellCount;
    public Vector3Int gridSize;
    public Vector3 cellSize;

    public Vector3Int startCell;

    public float[] distances;
    public int[] cellVertexIndex;
    public Mesh mesh;
    public bool dirty = true;

    public GameObject gameObject;
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;


    public readonly List<Vector3> vertices = new();
    public readonly List<int> triangles = new();

    public SDFChunk(Vector3Int coord, Vector3Int startCell, Vector3 localOrigin, Vector3Int cellCount, Vector3 cellSize)
    {
        this.coord = coord;
        this.startCell = startCell;
        this.localOrigin = localOrigin;
        this.cellCount = cellCount;
        this.gridSize = cellCount + Vector3Int.one;
        this.cellSize = cellSize;

        distances = new float[gridSize.x * gridSize.y * gridSize.z];
        cellVertexIndex = new int[cellCount.x * cellCount.y * cellCount.z];

        mesh = new Mesh();
        mesh.name = $"SDF Chunk {coord}";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.MarkDynamic();
    }

    public int DistanceIndex(int x, int y, int z)
    {
        return x + gridSize.x * (y + gridSize.y * z);
    }

    public float GetDistance(int x, int y, int z)
    {
        return distances[DistanceIndex(x, y, z)];
    }

    public int CellIndex(int x, int y, int z)
    {
        return x + cellCount.x * (y + cellCount.y * z);
    }

    public Vector3 GetLocalGridPosition(int x, int y, int z)
    {
        return localOrigin + new Vector3(
            x * cellSize.x,
            y * cellSize.y,
            z * cellSize.z
        );
    }

}