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
        Vector3Int res = layout.Resolution;
        Vector3Int minIdx = layout.WorldToIndex(resolved.min);
        Vector3Int maxIdx = layout.WorldToIndex(resolved.max);

        int minX = Mathf.Max(0, minIdx.x);
        int maxX = Mathf.Min(res.x, maxIdx.x + 1);
        int minY = Mathf.Max(0, minIdx.y);
        int maxY = Mathf.Min(res.y, maxIdx.y + 1);
        int minZ = Mathf.Max(0, minIdx.z);
        int maxZ = Mathf.Min(res.z, maxIdx.z + 1);

        if (minX >= maxX || minY >= maxY || minZ >= maxZ)
            return;

        int X = res.x, Y = res.y;
        bool fullCarve = Depth >= 1.0f;

        // Linearized loop — z outermost, x innermost for cache locality on flat buffer.
        for (int z = minZ; z < maxZ; z++)
        {
            int zBase = X * Y * z;
            for (int y = minY; y < maxY; y++)
            {
                int xyBase = zBase + X * y;
                if (fullCarve)
                {
                    for (int x = minX; x < maxX; x++)
                        target.SetDensity(x, y, z, float.MaxValue);
                }
                else
                {
                    for (int x = minX; x < maxX; x++)
                    {
                        float current = target.GetDensity(x, y, z);
                        target.SetDensity(x, y, z, Mathf.Max(current - Depth, float.MinValue));
                    }
                }
            }
        }
    }

    public override PersistentEditOperation Inverse() => null; // Carve is lossy — can't restore original density
}
