using UnityEngine;

[System.Serializable]
public class VoxelGridBuilder : VolumeBuilderBase<VoxelGrid>
{
    [Header("Grid")]
    public Vector3 gridExtent = new Vector3(4f, 4f, 4f);

    public Vector3Int gridSize = new Vector3Int(32, 32, 32);

    [Header("Options")]
    public bool uniformExtent = true;
    public bool uniformResolution = true;

    private Vector3 _lastGridExtent;
    private Vector3Int _lastGridSize;

    public override Bounds Bounds => new Bounds(Vector3.zero, gridExtent);

    public void Validate()
    {
        gridExtent.x = Mathf.Max(0.0001f, gridExtent.x);
        gridExtent.y = Mathf.Max(0.0001f, gridExtent.y);
        gridExtent.z = Mathf.Max(0.0001f, gridExtent.z);

        gridSize.x = Mathf.Max(2, gridSize.x);
        gridSize.y = Mathf.Max(2, gridSize.y);
        gridSize.z = Mathf.Max(2, gridSize.z);

        if (uniformExtent)
        {
            float value = GetChangedComponent(_lastGridExtent, gridExtent);
            gridExtent = new Vector3(value, value, value);
        }

        if (uniformResolution)
        {
            int value = GetChangedComponent(_lastGridSize, gridSize);
            gridSize = new Vector3Int(value, value, value);
        }

        _lastGridExtent = gridExtent;
        _lastGridSize = gridSize;
    }

    public override VoxelGrid Build(IScalarFieldSource source)
    {
        Validate();

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

        VoxelGrid grid = new VoxelGrid(
            safeSize,
            origin,
            cellSize
        );

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

                    values[index] = source.Evaluate(
                        new Vector3(px, py, pz)
                    );
                }
            }
        }

        return grid;
    }

    private float GetChangedComponent(Vector3 oldVal, Vector3 newVal)
    {
        if (!Mathf.Approximately(oldVal.x, newVal.x))
            return newVal.x;

        if (!Mathf.Approximately(oldVal.y, newVal.y))
            return newVal.y;

        if (!Mathf.Approximately(oldVal.z, newVal.z))
            return newVal.z;

        return newVal.x;
    }

    private int GetChangedComponent(Vector3Int oldVal, Vector3Int newVal)
    {
        if (oldVal.x != newVal.x)
            return newVal.x;

        if (oldVal.y != newVal.y)
            return newVal.y;

        if (oldVal.z != newVal.z)
            return newVal.z;

        return newVal.x;
    }
}