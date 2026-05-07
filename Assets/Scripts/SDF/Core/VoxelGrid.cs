using UnityEngine;

public class VoxelGrid : IVolumeData
{
    public Vector3Int GridSize { get; }
    public Vector3 Origin { get; }
    public Vector3 CellSize { get; }
    public float[] Values { get; }

    public Bounds Bounds
    {
        get
        {
            Vector3 size = new Vector3(
                CellSize.x * (GridSize.x - 1),
                CellSize.y * (GridSize.y - 1),
                CellSize.z * (GridSize.z - 1)
            );

            return new Bounds(Origin + size * 0.5f, size);
        }
    }

    public VoxelGrid(Vector3Int gridSize, Vector3 origin, Vector3 cellSize)
    {
        GridSize = gridSize;
        Origin = origin;
        CellSize = cellSize;
        Values = new float[gridSize.x * gridSize.y * gridSize.z];
    }

    public int GetIndex(int x, int y, int z)
    {
        return x + GridSize.x * (y + GridSize.y * z);
    }

    public float GetValue(int x, int y, int z)
    {
        return Values[GetIndex(x, y, z)];
    }

    public void SetValue(int x, int y, int z, float value)
    {
        Values[GetIndex(x, y, z)] = value;
    }

    public Vector3 GetWorldPosition(int x, int y, int z)
    {
        return new Vector3(
            Origin.x + x * CellSize.x,
            Origin.y + y * CellSize.y,
            Origin.z + z * CellSize.z
        );
    }
}