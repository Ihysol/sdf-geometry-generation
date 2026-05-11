using UnityEngine;

[System.Serializable]
public class OctreeVolumeBuilder : IVolumeBuilder<OctreeVolume>
{
    public Vector3 center = Vector3.zero;
    public Vector3 size = new Vector3(4f, 4f, 4f);

    public int maxDepth = 6;

    public Bounds Bounds => new Bounds(center, size);

    public OctreeVolume Build(IScalarFieldSource source)
    {
        OctreeNode root = BuildNode(source, Bounds, 0);
        return new OctreeVolume(root, Bounds, maxDepth);
    }

    private OctreeNode BuildNode(IScalarFieldSource source, Bounds bounds, int depth)
    {
        OctreeNode node = new OctreeNode(bounds);

        float[] corners = SampleCorners(source, bounds);

        bool hasNegative = false;
        bool hasPositive = false;

        for (int i = 0; i < corners.Length; i++)
        {
            if (corners[i] < 0f)
                hasNegative = true;
            else
                hasPositive = true;
        }

        bool containsSurface = hasNegative && hasPositive;

        node.CenterValue = source.Evaluate(bounds.center);
        node.ContainsSurface = containsSurface;

        if (depth >= maxDepth || !containsSurface)
        {
            node.IsLeaf = true;
            return node;
        }

        node.IsLeaf = false;
        node.Children = new OctreeNode[8];

        Vector3 childSize = bounds.size * 0.5f;
        Vector3 min = bounds.min;

        int iChild = 0;

        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 childCenter = min + new Vector3(
                (x + 0.5f) * childSize.x,
                (y + 0.5f) * childSize.y,
                (z + 0.5f) * childSize.z
            );

            Bounds childBounds = new Bounds(childCenter, childSize);
            node.Children[iChild++] = BuildNode(source, childBounds, depth + 1);
        }

        return node;
    }

    private float[] SampleCorners(IScalarFieldSource source, Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new float[]
        {
            source.Evaluate(new Vector3(min.x, min.y, min.z)),
            source.Evaluate(new Vector3(max.x, min.y, min.z)),
            source.Evaluate(new Vector3(max.x, min.y, max.z)),
            source.Evaluate(new Vector3(min.x, min.y, max.z)),

            source.Evaluate(new Vector3(min.x, max.y, min.z)),
            source.Evaluate(new Vector3(max.x, max.y, min.z)),
            source.Evaluate(new Vector3(max.x, max.y, max.z)),
            source.Evaluate(new Vector3(min.x, max.y, max.z)),
        };
    }
}