using System.Collections.Generic;
using UnityEngine;

/// <summary>Per-chunk buffer snapshot to bound replay cost. See ADR-006.</summary>
public struct EditCheckpoint
{
    public ChunkCoord coord;
    public int operationGeneration; // Only operations with generation > this need replay
}

/// <summary>ADR-004: Replayable persistent edits applied after sampling the Authoring Composition.</summary>
public class PersistentEditLayer
{
    private readonly List<PersistentEditOperation> _history = new();
    private int _undoCursor; // Index of "next" slot — operations before cursor are active
    private int _nextGeneration;

    /// <summary>Total active operations (before undo cursor).</summary>
    public int OperationCount => _undoCursor;

    /// <summary>Can undo? (active operations exist).</summary>
    public bool CanUndo => _undoCursor > 0;

    /// <summary>Can redo? (undone operations exist after cursor).</summary>
    public bool CanRedo => _undoCursor < _history.Count;

    /// <summary>Add an operation and advance the undo cursor. Redo stack is discarded.</summary>
    public void Add(PersistentEditOperation op)
    {
        // Discard redo history when new operation is added
        _history.RemoveRange(_undoCursor, _history.Count - _undoCursor);

        op.Generation = _nextGeneration++;
        _history.Add(op);
        _undoCursor++;
    }

    /// <summary>Undo the most recent operation. Returns the undone operation or null.</summary>
    public PersistentEditOperation Undo()
    {
        if (!CanUndo) return null;
        _undoCursor--;
        return _history[_undoCursor];
    }

    /// <summary>Redo the last undone operation. Returns the redone operation or null.</summary>
    public PersistentEditOperation Redo()
    {
        if (!CanRedo) return null;
        var op = _history[_undoCursor];
        _undoCursor++;
        return op;
    }

    /// <summary>Replay all active operations intersecting the given world region over the target view.
    /// Linear scan — OK for Seam 1, replaced by spatial index later (ADR-016).</summary>
    public void ReplayRegion(IVolumeView target, Bounds worldBounds, Transform processorTransform)
    {
        for (int i = 0; i < _undoCursor; i++)
        {
            var op = _history[i];

            // Check if operation has a checkpoint — skip older ones
            ChunkCoord regionChunk = GetCoveringChunk(worldBounds, target.Layout);
            if (_checkpoints.TryGetValue(regionChunk, out var cp) && op.Generation <= cp.operationGeneration)
                continue;

            // Linear intersection test
            if (op.IntersectsWorld(worldBounds, processorTransform))
                op.Replay(target);
        }
    }

    /// <summary>Remove the most recent operation (legacy alias for Undo). Returns the removed operation or null.</summary>
    [System.Obsolete("Use Undo() instead")]
    public PersistentEditOperation UndoLast() => Undo();

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

    /// <summary>Clear all operations, checkpoints, and undo state (e.g., on layout migration — ADR-014).</summary>
    public void Clear()
    {
        _history.Clear();
        _undoCursor = 0;
        _checkpoints.Clear();
    }

    private static ChunkCoord GetCoveringChunk(Bounds bounds, VolumeLayout layout)
    {
        int cs = layout.ChunkSize;
        Vector3Int centerIndex = layout.WorldToIndex(bounds.center);
        return new ChunkCoord(centerIndex.x / cs, centerIndex.y / cs, centerIndex.z / cs);
    }
}
