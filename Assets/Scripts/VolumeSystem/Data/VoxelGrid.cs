using UnityEngine;

using System.Collections.Generic;

public class VoxelGrid : IVolumeData, IChunkLayoutVolume
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

    /// <summary>Creates a dense voxel grid with one scalar value per grid point.</summary>
    public VoxelGrid(Vector3Int gridSize, Vector3 origin, Vector3 cellSize)
    {
        GridSize = gridSize;
        Origin = origin;
        CellSize = cellSize;
        Values = new float[gridSize.x * gridSize.y * gridSize.z];
    }

    /// <summary>Converts 3D grid coordinates into the flat values array index.</summary>
    public int GetIndex(int x, int y, int z)
    {
        return x + GridSize.x * (y + GridSize.y * z);
    }

    /// <summary>Reads a scalar value at the given grid coordinate.</summary>
    public float GetValue(int x, int y, int z)
    {
        return Values[GetIndex(x, y, z)];
    }

    /// <summary>Writes a scalar value at the given grid coordinate.</summary>
    public void SetValue(int x, int y, int z, float value)
    {
        Values[GetIndex(x, y, z)] = value;
    }

    /// <summary>Returns the world position for a grid coordinate.</summary>
    public Vector3 GetWorldPosition(int x, int y, int z)
    {
        return new Vector3(
            Origin.x + x * CellSize.x,
            Origin.y + y * CellSize.y,
            Origin.z + z * CellSize.z
        );
    }

    public void BuildChunkBounds(ChunkingSettings settings, List<Bounds> output)
    {
        output.Clear();

        Vector3Int chunkCount = settings.voxelChunkCount;
        chunkCount.x = Mathf.Max(1, chunkCount.x);
        chunkCount.y = Mathf.Max(1, chunkCount.y);
        chunkCount.z = Mathf.Max(1, chunkCount.z);

        Bounds bounds = Bounds;
        Vector3 chunkSize = new Vector3(
            bounds.size.x / chunkCount.x,
            bounds.size.y / chunkCount.y,
            bounds.size.z / chunkCount.z
        );

        for (int x = 0; x < chunkCount.x; x++)
            for (int y = 0; y < chunkCount.y; y++)
                for (int z = 0; z < chunkCount.z; z++)
                {
                    Vector3 center = bounds.min + new Vector3(
                        (x + 0.5f) * chunkSize.x,
                        (y + 0.5f) * chunkSize.y,
                        (z + 0.5f) * chunkSize.z
                    );

                    output.Add(new Bounds(center, chunkSize));
                }
    }
}
