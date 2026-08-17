using System.Collections.Generic;
using UnityEngine;

/// <summary>Creates or destroys discrete grid cells. Replays cleanly during rebuilds.</summary>
public class CellOperation : PersistentEditOperation
{
    /// <summary>Grid indices affected by this operation.</summary>
    public List<Vector3Int> Cells { get; private set; } = new();

    /// <summary>True = fill cell (add geometry), False = carve cell (remove geometry).</summary>
    public bool Fill { get; private set; }

    public CellOperation(List<Vector3Int> cells, EditAnchor anchor, bool fill)
    {
        Type = fill ? EditType.Fill : EditType.Carve;
        Cells = cells;
        Anchor = anchor;
        Fill = fill;

        // Compute bounding region for intersection tests
        if (cells.Count > 0)
        {
            Vector3 min = cells[0];
            Vector3 max = cells[0];
            foreach (var c in cells)
            {
                min = Vector3.Min(min, c);
                max = Vector3.Max(max, c);
            }
            Region = new Bounds((min + max) * 0.5f, (max - min) + Vector3.one);
        }
    }

    public override void Replay(IVolumeView target)
    {
        if (!Anchor.ResolveRegion(Region, null, out var resolved))
            return;

        var layout = target.Layout;

        foreach (var cell in Cells)
        {
            Vector3Int idx = new Vector3Int(
                Mathf.Clamp(cell.x, 0, layout.Resolution.x - 1),
                Mathf.Clamp(cell.y, 0, layout.Resolution.y - 1),
                Mathf.Clamp(cell.z, 0, layout.Resolution.z - 1)
            );

            if (!layout.IsInside(idx))
                continue;

            target.SetDensity(idx.x, idx.y, idx.z, Fill ? 0f : float.MaxValue);
        }
    }

    public override PersistentEditOperation Inverse() => null; // Cell edit is lossy
}
