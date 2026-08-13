using UnityEngine;

/// <summary>Carves a region out of the volume by setting density to MaxValue (empty space).</summary>
public class CarveOperation : PersistentEditOperation
{
    public float Depth { get; set; } = 1.0f; // 0-1, 1 = fully hollow

    public CarveOperation(Bounds region, EditAnchor anchor, float depth = 1.0f)
    {
        Type = EditType.Carve;
        Region = region;
        Anchor = anchor;
        Depth = depth;
    }

    public override void Replay(IVolumeView target)
    {
        if (!Anchor.ResolveRegion(Region, null, out var resolved))
            return;

        var layout = target.Layout;
        Vector3Int minIdx = layout.WorldToIndex(resolved.min);
        Vector3Int maxIdx = layout.WorldToIndex(resolved.max);

        for (int x = Mathf.Max(0, minIdx.x); x < Mathf.Min(layout.Resolution.x, maxIdx.x + 1); x++)
        {
            for (int y = Mathf.Max(0, minIdx.y); y < Mathf.Min(layout.Resolution.y, maxIdx.y + 1); y++)
            {
                for (int z = Mathf.Max(0, minIdx.z); z < Mathf.Min(layout.Resolution.z, maxIdx.z + 1); z++)
                {
                    float current = target.GetDensity(x, y, z);
                    if (Depth >= 1.0f)
                        target.SetDensity(x, y, z, float.MaxValue); // Fully carved
                    else
                        target.SetDensity(x, y, z, Mathf.Max(current - Depth, float.MinValue));
                }
            }
        }
    }

    public override PersistentEditOperation Inverse() => null; // Carve is lossy — can't restore original density
}
