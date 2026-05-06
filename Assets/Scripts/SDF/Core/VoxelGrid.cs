using UnityEngine;

public class VoxelGrid
{
    public Vector3 Origin { get; private set; }
    public Vector3 CellSize { get; private set; }
    public Vector3Int GridSize { get; private set; }
    public float[] Distances { get; private set; }

    public VoxelGrid(Vector3 origin, Vector3 cellSize, Vector3Int gridSize)
    {
        Origin = origin;
        CellSize = cellSize;

        GridSize = new Vector3Int(Mathf.Max(2, gridSize.x), Mathf.Max(2, gridSize.y), Mathf.Max(2, gridSize.z));
        Distances = new float[GridSize.x * GridSize.y * GridSize.z];
    }

    public int Index(int x, int y, int z)
    {
        return x + GridSize.x * (y + GridSize.y * z);
    }

    public float Get(int x, int y, int z)
    {
        return Distances[Index(x, y, z)];
    }

    public void Set(int x, int y, int z, float value)
    {
        Distances[Index(x, y, z)] = value;
    }

    public Vector3 GetPosition(int x, int y, int z)
    {
        return Origin + new Vector3(x * CellSize.x, y * CellSize.y, z * CellSize.z);
    }
}