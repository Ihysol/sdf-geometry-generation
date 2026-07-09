using UnityEngine;

public struct VolumeLayout
{
    public Vector3Int Resolution;
    public float CellSize;
    public Vector3 Origin;
    public int ChunkSize;
    public float IsoLevel;

    public static VolumeLayout FromBounds(Bounds bounds, Vector3Int resolution)
    {
        return new VolumeLayout
        {
            Resolution = resolution,
            CellSize = bounds.size.x / resolution.x,
            Origin = bounds.min,
            ChunkSize = 16,
            IsoLevel = 0f
        };
    }

    public Vector3 IndexToWorld(Vector3Int index)
    {
        float halfCell = CellSize * 0.5f;
        return Origin + new Vector3(index.x + 0.5f, index.y + 0.5f, index.z + 0.5f) * CellSize;
    }

    public Vector3 WorldToCell(Vector3 world)
    {
        return (world - Origin) / CellSize;
    }

    public Vector3Int WorldToIndex(Vector3 world)
    {
        Vector3 cell = WorldToCell(world);
        return new Vector3Int(
            Mathf.FloorToInt(cell.x),
            Mathf.FloorToInt(cell.y),
            Mathf.FloorToInt(cell.z)
        );
    }

    public bool IsInside(Vector3Int index)
    {
        return index.x >= 0 && index.x < Resolution.x &&
               index.y >= 0 && index.y < Resolution.y &&
               index.z >= 0 && index.z < Resolution.z;
    }

    public int IndexToOffset(Vector3Int index)
    {
        return index.z * Resolution.x * Resolution.y +
               index.y * Resolution.x +
               index.x;
    }

    public Vector3Int OffsetToIndex(int offset)
    {
        int x = offset % Resolution.x;
        int remaining = offset / Resolution.x;
        int y = remaining % Resolution.y;
        int z = remaining / Resolution.y;
        return new Vector3Int(x, y, z);
    }

    public int TotalCells => Resolution.x * Resolution.y * Resolution.z;
}
