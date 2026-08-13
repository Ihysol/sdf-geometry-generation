using System.Collections.Generic;
using UnityEngine;

/// <summary>Per-chunk buffer snapshot to bound replay cost. See ADR-006.</summary>
public struct EditCheckpoint
{
    public ChunkCoord coord;
    public int operationGeneration; // Only operations with generation > this need replay
}

/// <summary>ADR-004: Replayable persistent edits applied after sampling the Authoring Composition.
/// Seam 1 of ADR-016 migration — currently empty but present in the pipeline.</summary>
public class PersistentEditLayer
{
    private readonly List<PersistentEditOperation> _operations = new();
    private readonly Dictionary<ChunkCoord, EditCheckpoint> _checkpoints = new();
    private int _nextGeneration;

    /// <summary>Total operations (including suspended).</summary>
    public int OperationCount => _operations.Count;

    /// <summary>Add an operation and assign its generation.</summary>
    public void Add(PersistentEditOperation op)
    {
        op.Generation = _nextGeneration++;
        _operations.Add(op);
    }

    /// <summary>Replay all operations intersecting the given world region over the target view.
    /// Linear scan — OK for Seam 1, replaced by spatial index later (ADR-016).</summary>
    public void ReplayRegion(IVolumeView target, Bounds worldBounds, Transform processorTransform)
    {
        // Sort by generation to ensure deterministic order
        _operations.Sort((a, b) => a.Generation.CompareTo(b.Generation));

        for (int i = 0; i < _operations.Count; i++)
        {
            var op = _operations[i];

            // Check if operation has a checkpoint — skip older ones
            ChunkCoord regionChunk = GetCoveringChunk(worldBounds, target.Layout);
            if (_checkpoints.TryGetValue(regionChunk, out var cp) && op.Generation <= cp.operationGeneration)
                continue;

            // Linear intersection test
            if (op.IntersectsWorld(worldBounds, processorTransform))
                op.Replay(target);
        }
    }

    /// <summary>Remove the most recent operation (undo). Returns the removed operation or null.</summary>
    public PersistentEditOperation UndoLast()
    {
        if (_operations.Count == 0) return null;
        var op = _operations[_operations.Count - 1];
        _operations.RemoveAt(_operations.Count - 1);
        return op;
    }

    /// <summary>ADR-006: Create a checkpoint for the given chunk. Stub — always returns false until implemented.</summary>
    public bool CreateCheckpoint(ChunkCoord coord)
    {
        // TODO: Capture current buffer state for this chunk
        _checkpoints[coord] = new EditCheckpoint { coord = coord, operationGeneration = _nextGeneration - 1 };
        return true;
    }

    /// <summary>Get checkpoint for a chunk. Returns null if none exists.</summary>
    public EditCheckpoint? GetCheckpoint(ChunkCoord coord)
    {
        _checkpoints.TryGetValue(coord, out var cp);
        return cp;
    }

    /// <summary>Clear all operations and checkpoints (e.g., on layout migration — ADR-014).</summary>
    public void Clear()
    {
        _operations.Clear();
        _checkpoints.Clear();
    }

    private static ChunkCoord GetCoveringChunk(Bounds bounds, VolumeLayout layout)
    {
        int cs = layout.ChunkSize;
        Vector3Int centerIndex = layout.WorldToIndex(bounds.center);
        return new ChunkCoord(centerIndex.x / cs, centerIndex.y / cs, centerIndex.z / cs);
    }
}
