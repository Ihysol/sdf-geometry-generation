using UnityEngine;

[System.Serializable]
public class VoxelGridSampler : IVolumeSampler
{
    [Header("Builder")]
    public VoxelGridBuilder builder = new();

    public VoxelGrid Volume { get; private set; }

    IVolumeData IVolumeSampler.Volume => Volume;

    public bool IsDirty { get; private set; } = true;

    /// <summary>Marks the sampled voxel grid as stale.</summary>
    public void MarkDirty()
    {
        IsDirty = true;
    }

    /// <summary>Rebuilds the voxel grid from the given scalar field.</summary>
    public void RebuildVolume(IScalarFieldSource source)
    {
        if (source == null)
        {
            Volume = null;
            IsDirty = false;
            return;
        }

        builder?.Validate();

        Volume = builder.Build(source);
        IsDirty = false;
    }

    public bool RebuildVolumeRegion(IScalarFieldSource source, Bounds dirtyBounds, int paddingCells = 1)
    {
        if (source == null)
        {
            Volume = null;
            IsDirty = false;
            return false;
        }

        builder?.Validate();

        if (Volume == null || !IsCompatibleWithCurrentBuilder(Volume))
        {
            RebuildVolume(source);
            return false;
        }

        VoxelGrid grid = Volume;
        Vector3Int size = grid.GridSize;
        Vector3 origin = grid.Origin;
        Vector3 cell = grid.CellSize;

        Vector3 min = dirtyBounds.min - cell * Mathf.Max(0, paddingCells);
        Vector3 max = dirtyBounds.max + cell * Mathf.Max(0, paddingCells);

        Vector3 localMin = min - origin;
        Vector3 localMax = max - origin;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(localMin.x / cell.x), 0, size.x - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(localMin.y / cell.y), 0, size.y - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(localMin.z / cell.z), 0, size.z - 1);

        int x1 = Mathf.Clamp(Mathf.CeilToInt(localMax.x / cell.x), 0, size.x - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(localMax.y / cell.y), 0, size.y - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt(localMax.z / cell.z), 0, size.z - 1);

        float[] values = grid.Values;

        for (int z = z0; z <= z1; z++)
        {
            float pz = origin.z + z * cell.z;

            for (int y = y0; y <= y1; y++)
            {
                float py = origin.y + y * cell.y;
                int rowBase = size.x * (y + size.y * z);

                for (int x = x0; x <= x1; x++)
                {
                    float px = origin.x + x * cell.x;
                    int index = x + rowBase;
                    values[index] = source.Evaluate(new Vector3(px, py, pz));
                }
            }
        }

        IsDirty = false;
        return true;
    }

    private bool IsCompatibleWithCurrentBuilder(VoxelGrid volume)
    {
        if (builder == null || volume == null)
            return false;

        Vector3Int targetSize = builder.gridSize;
        Vector3 targetExtent = builder.gridExtent;

        Vector3 targetOrigin = -targetExtent * 0.5f;
        Vector3 targetCell = new Vector3(
            targetExtent.x / (targetSize.x - 1),
            targetExtent.y / (targetSize.y - 1),
            targetExtent.z / (targetSize.z - 1)
        );

        if (volume.GridSize != targetSize)
            return false;

        return volume.Origin == targetOrigin && volume.CellSize == targetCell;
    }
}
