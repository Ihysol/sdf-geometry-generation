using UnityEngine;

/// <summary>ADR-015: Coordinate anchor for persistent edits.</summary>
public enum EditAnchorType
{
    World,
    Processor,
    Object
}

/// <summary>Resolves a persistent edit's region into world space at replay time.</summary>
[System.Serializable]
public struct EditAnchor
{
    public EditAnchorType type;
    
    /// <summary>Stable object identity (GUID or hash) — ignored for World/Processor anchors.</summary>
    public string objectId;

    /// <summary>Empty anchor — resolves to World at origin.</summary>
    public static EditAnchor DefaultWorld => new EditAnchor { type = EditAnchorType.World };

    /// <summary>Resolve region from processor-local to world space. Returns false if Object anchor is unresolved.</summary>
    public bool ResolveRegion(Bounds localOrWorldRegion, Transform processorTransform, out Bounds worldRegion)
    {
        switch (type)
        {
            case EditAnchorType.World:
                worldRegion = localOrWorldRegion;
                return true;

            case EditAnchorType.Processor:
                Matrix4x4 m = processorTransform.localToWorldMatrix;
                worldRegion = TransformBounds(localOrWorldRegion, m);
                return true;

            case EditAnchorType.Object:
                // TODO: Look up object by objectId — for now suspend if unresolved
                // ADR-015: Object anchors must be explicitly resolved, not silently fall back.
                worldRegion = default;
                return false;

            default:
                worldRegion = default;
                return false;
        }
    }

    private static Bounds TransformBounds(Bounds b, Matrix4x4 m)
    {
        // Transform 8 corners
        Vector3[] corners = new Vector3[8];
        corners[0] = new Vector3(b.min.x, b.min.y, b.min.z);
        corners[1] = new Vector3(b.max.x, b.min.y, b.min.z);
        corners[2] = new Vector3(b.min.x, b.max.y, b.min.z);
        corners[3] = new Vector3(b.max.x, b.max.y, b.min.z);
        corners[4] = new Vector3(b.min.x, b.min.y, b.max.z);
        corners[5] = new Vector3(b.max.x, b.min.y, b.max.z);
        corners[6] = new Vector3(b.min.x, b.max.y, b.max.z);
        corners[7] = new Vector3(b.max.x, b.max.y, b.max.z);

        Vector3 min = m.MultiplyPoint(corners[0]);
        Vector3 max = min;
        for (int i = 1; i < 8; i++)
        {
            Vector3 c = m.MultiplyPoint(corners[i]);
            if (c.x < min.x) min.x = c.x; else if (c.x > max.x) max.x = c.x;
            if (c.y < min.y) min.y = c.y; else if (c.y > max.y) max.y = c.y;
            if (c.z < min.z) min.z = c.z; else if (c.z > max.z) max.z = c.z;
        }

        return new Bounds((min + max) * 0.5f, max - min);
    }
}
