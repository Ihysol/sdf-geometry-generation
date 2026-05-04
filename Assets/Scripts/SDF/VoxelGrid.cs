using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class VoxelGrid
{
    public Vector3 Origin { get; }
    public Vector3 CellSize { get; }
    public Vector3Int GridSize { get; }
    public float[] Distances { get; }

    public VoxelGrid(Vector3 origin, Vector3 cellSize, Vector3Int gridSize)
    {
        Origin = origin;
        CellSize = cellSize;
        GridSize = gridSize;
        Distances = new float[gridSize.x * GridSize.y * gridSize.z];
    }

    public int Index(int x, int y, int z)
    {
        return x * GridSize.x * (y + GridSize.y * z);
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