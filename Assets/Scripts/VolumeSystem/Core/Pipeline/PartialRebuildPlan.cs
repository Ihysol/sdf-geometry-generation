using UnityEngine;

/// <summary>
/// ADR-019: The single region policy for partial rebuilds. Computes, from one dirty
/// region + layout + the active mesher's declared read halo, BOTH the grid-cell region
/// to resample (<see cref="SampleRegion"/>) and the chunk range to remesh
/// (<see cref="RemeshMin"/> / <see cref="RemeshMax"/>).
///
/// Deriving both from the same base (the chunk-expanded dirty region) makes
/// sampling/remesh drift structurally impossible — the duplication that previously let a
/// stale read-halo escape (ADR-019) no longer exists.
///
/// The struct is allocation-free (all value types) so it is safe on the per-edit hot path.
/// </summary>
public struct PartialRebuildPlan
{
    /// <summary>Grid cells to resample — covers every remeshed chunk's cells plus the mesher's read halo.</summary>
    public BoundsInt SampleRegion;

    /// <summary>Lowest chunk index (inclusive) to remesh, clamped to the chunk grid.</summary>
    public Vector3Int RemeshMin;

    /// <summary>Highest chunk index (inclusive) to remesh, clamped to the chunk grid.</summary>
    public Vector3Int RemeshMax;

    /// <summary>
    /// Build the full plan (sampling + remeshing).
    /// </summary>
    /// <param name="region">Dirty region in grid-cell indices, already clamped to the grid by the caller.</param>
    /// <param name="layout">Grid resolution and chunk size.</param>
    /// <param name="chunkGrid">Chunk grid extent per axis = ceil(resolution / chunkSize).</param>
    /// <param name="haloCells">Active mesher's declared read halo (<see cref="IVolumeMesher.ReadHaloCells"/>).</param>
    public static PartialRebuildPlan Create(
        BoundsInt region, VolumeLayout layout, Vector3Int chunkGrid, int haloCells)
    {
        int cs = layout.ChunkSize;
        Vector3Int res = layout.Resolution;

        Vector3Int remMin, remMax;
        if (cs <= 0)
        {
            // Degenerate layout: a single chunk covers the whole grid.
            remMin = Vector3Int.zero;
            remMax = Vector3Int.zero;
        }
        else
        {
            remMin = ChunkOf(region.position, cs) - Vector3Int.one;
            remMax = ChunkOf(region.position + region.size - Vector3Int.one, cs) + Vector3Int.one;

            // Clamp to the chunk grid. (res-1)/cs == ceil(res/cs)-1, so this equals the
            // DirtyChunkSystem grid clamp — both stages share the same bound.
            remMin = Vector3Int.Max(remMin, Vector3Int.zero);
            remMax = Vector3Int.Min(remMax, chunkGrid - Vector3Int.one);
        }

        // Sample region: the remesh chunk range (in cells), expanded by the mesher's read
        // halo, clamped to the grid. Every remeshed chunk's cells fall inside the chunk
        // range; its halo reads extend at most `haloCells` past it — both are covered.
        int halo = Mathf.Max(0, haloCells);
        Vector3Int sMin = remMin * cs - Vector3Int.one * halo;
        Vector3Int sMaxExclusive = (remMax + Vector3Int.one) * cs + Vector3Int.one * halo;

        sMin = Vector3Int.Max(sMin, Vector3Int.zero);
        sMaxExclusive = Vector3Int.Min(sMaxExclusive, res);

        Vector3Int sSize = sMaxExclusive - sMin;
        sSize.x = Mathf.Max(0, sSize.x);
        sSize.y = Mathf.Max(0, sSize.y);
        sSize.z = Mathf.Max(0, sSize.z);

        return new PartialRebuildPlan
        {
            SampleRegion = new BoundsInt(sMin, sSize),
            RemeshMin = remMin,
            RemeshMax = remMax
        };
    }

    /// <summary>
    /// Compute only the remesh chunk range (used by <c>DirtyChunkSystem.MarkDirty(BoundsInt)</c>
    /// for in-place operations that have no resampling stage). Same ±1-chunk policy as
    /// <see cref="Create"/>, so the remesh side is always derived from one place.
    /// </summary>
    public static (Vector3Int min, Vector3Int max) RemeshRange(
        BoundsInt region, Vector3Int chunkGrid, int chunkSize)
    {
        if (chunkSize <= 0)
        {
            var z = Vector3Int.zero;
            return (z, z);
        }

        Vector3Int min = ChunkOf(region.position, chunkSize) - Vector3Int.one;
        Vector3Int max = ChunkOf(region.position + region.size - Vector3Int.one, chunkSize) + Vector3Int.one;

        min = Vector3Int.Max(min, Vector3Int.zero);
        max = Vector3Int.Min(max, chunkGrid - Vector3Int.one);

        return (min, max);
    }

    private static Vector3Int ChunkOf(Vector3Int cellIndex, int cs)
    {
        return new Vector3Int(
            Mathf.FloorToInt((float)cellIndex.x / cs),
            Mathf.FloorToInt((float)cellIndex.y / cs),
            Mathf.FloorToInt((float)cellIndex.z / cs));
    }
}
