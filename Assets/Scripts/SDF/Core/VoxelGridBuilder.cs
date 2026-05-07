using UnityEngine;

[System.Serializable]
public class VoxelGridBuilder : IVolumeBuilder<VoxelGrid>
{
    public Vector3 gridExtent = new Vector3(4f, 4f, 4f);
    public Vector3Int gridSize = new Vector3Int(32, 32, 32);

    public VoxelGrid Build(IScalarFieldSource source)
    {
        Vector3Int safeSize = new Vector3Int(
            Mathf.Max(2, gridSize.x),
            Mathf.Max(2, gridSize.y),
            Mathf.Max(2, gridSize.z)
        );

        Vector3 safeExtent = new Vector3(
            Mathf.Max(0.0001f, gridExtent.x),
            Mathf.Max(0.0001f, gridExtent.y),
            Mathf.Max(0.0001f, gridExtent.z)
        );

        Vector3 origin = -safeExtent * 0.5f;

        Vector3 cellSize = new Vector3(
            safeExtent.x / (safeSize.x - 1),
            safeExtent.y / (safeSize.y - 1),
            safeExtent.z / (safeSize.z - 1)
        );

        VoxelGrid grid = new VoxelGrid(safeSize, origin, cellSize);
        float[] values = grid.Values;

        for (int z = 0; z < safeSize.z; z++)
        {
            float pz = origin.z + z * cellSize.z;

            for (int y = 0; y < safeSize.y; y++)
            {
                float py = origin.y + y * cellSize.y;
                int rowBase = safeSize.x * (y + safeSize.y * z);

                for (int x = 0; x < safeSize.x; x++)
                {
                    float px = origin.x + x * cellSize.x;
                    int index = x + rowBase;

                    values[index] = source.Evaluate(new Vector3(px, py, pz));
                }
            }
        }

        return grid;
    }
}