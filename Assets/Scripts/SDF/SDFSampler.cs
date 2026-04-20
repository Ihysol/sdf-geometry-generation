using Unity.VectorGraphics;
using UnityEngine;

public class SDFSampler : MonoBehaviour
{
    public enum ShapeMode
    {
        Sphere,
        Box
    }

    [Header("Shape")]
    public ShapeMode shapeMode = ShapeMode.Sphere;
    public float sphereRadius = 1.5f;
    public Vector3 boxHalfExtents = new Vector3(1f, 1f, 1f);

    [Header("Grid")]
    public bool useAutomaticBounds = true;
    public Vector3 gridExtent = new Vector3(4f, 4f, 4f);
    public float boundsPadding = 0.2f;
    public int resolution = 10;

    public SDFSample[] Samples { get; private set; }
    public Vector3Int GridSize { get; private set; }
    public float CellSize { get; private set; }

    private ISDF _sdf;

    public void RebuildSamples()
    {
        BuildSDF();

        Vector3 effectiveGridExtent = GetEffectiveGridExtent();

        CellSize = 1f / resolution;

        GridSize = new Vector3Int(
            Mathf.CeilToInt(effectiveGridExtent.x * resolution),
            Mathf.CeilToInt(effectiveGridExtent.y * resolution),
            Mathf.CeilToInt(effectiveGridExtent.z * resolution)
        );

        int totalCount = GridSize.x * GridSize.y * GridSize.z;
        Samples = new SDFSample[totalCount];

        Vector3 origin = transform.position - effectiveGridExtent * 0.5f;

        for (int x = 0; x < GridSize.x; x++)
        {
            for (int y = 0; y < GridSize.y; y++)
            {
                for (int z = 0; z < GridSize.z; z++)
                {
                    int index = GetIndex(x, y, z);

                    Vector3 worldPos = origin + new Vector3(x, y, z) * CellSize;
                    Vector3 localPos = transform.InverseTransformPoint(worldPos);
                    float distance = _sdf.Evaluate(localPos);

                    Samples[index] = new SDFSample
                    {
                        WorldPosition = worldPos,
                        LocalPosition = localPos,
                        Distance = distance
                    };
                }
            }
        }
    }

    private void BuildSDF()
    {
        switch (shapeMode)
        {
            case ShapeMode.Box:
                _sdf = new BoxSDF(Vector3.zero, boxHalfExtents);
                break;

            case ShapeMode.Sphere:
            default:
                _sdf = new SphereSDF(Vector3.zero, sphereRadius);
                break;
        }
    }

    public int GetIndex(int x, int y, int z)
    {
        return x + GridSize.x * (y + GridSize.y * z);
    }

    public bool IsValidCoordinate(int x, int y, int z)
    {
        return x >= 0 && x < GridSize.x &&
               y >= 0 && y < GridSize.y &&
               z >= 0 && z < GridSize.z;
    }

    public SDFSample GetSample(int x, int y, int z)
    {
        return Samples[GetIndex(x, y, z)];
    }
    private void OnValidate()
    {
        if (resolution < 1)
            resolution = 1;
    }

    private Vector3 GetEffectiveGridExtent()
    {
        if (!useAutomaticBounds)
            return gridExtent;

        switch(shapeMode)
        {
            case ShapeMode.Box:
                return(boxHalfExtents + Vector3.one * boundsPadding) *2f;

            case ShapeMode.Sphere:
            default:
                float r = sphereRadius + boundsPadding;
                return new Vector3(r * 2f, r* 2f, r*2f);
        }
    }
}