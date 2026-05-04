using UnityEditor.UI;
using UnityEngine;

public class SDFSampler : MonoBehaviour
{
    [Header("SDF")]
    public SDFNode sdf;

    [Header("Volume")]
    public Vector3 gridExtent = new Vector3(4f, 4f, 4f);
    public Vector3Int gridSize = new Vector3Int(32, 32, 32);

    public VoxelGrid Volume { get; private set; }
    public bool IsDirty { get; private set; } = true;

    private void Awake()
    {

    }

    private void OnValidate()
    {
        gridSize.x = Mathf.Max(2, gridSize.x);
        gridSize.y = Mathf.Max(2, gridSize.y);
        gridSize.z = Mathf.Max(2, gridSize.z);
        MarkDirty();
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void RebuildVolume()
    {
        if (sdf == null)
        {
            Volume = null;
            IsDirty = false;
            return;
        }

        Vector3 origin = -gridExtent * 0.5f;
        Vector3 cellSize = new Vector3(
            gridExtent.x / (gridSize.x - 1),
            gridExtent.y / (gridSize.y - 1),
            gridExtent.z / (gridSize.z - 1)
        );

        Volume = new VoxelGrid(origin, cellSize, gridSize);

        for (int z = 0; z < gridSize.z; z++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    Vector3 p = Volume.GetPosition(x, y, z);
                    float d = sdf.Evaluate(p);
                    Volume.Set(x, y, z, d);
                }
            }
        }
        IsDirty = false;
    }


    public float EvaluateLocal(Vector3 p)
    {
        if (sdf == null)
            return 1f;

        return sdf.Evaluate(p);
    }

    public Vector3 EstimateNormalLocal(Vector3 p)
    {
        float h = Mathf.Max(0.0001f, Mathf.Min(gridExtent.x, gridExtent.y, gridExtent.z) / 512f);
        float dx = EvaluateLocal(p + new Vector3(h, 0f, 0f)) - EvaluateLocal(p - new Vector3(h, 0f, 0f));
        float dy = EvaluateLocal(p + new Vector3(0f, h, 0f)) - EvaluateLocal(p - new Vector3(0f, h, 0f));
        float dz = EvaluateLocal(p + new Vector3(0f, 0f, h)) - EvaluateLocal(p - new Vector3(0f, 0f, h));

        Vector3 n = new Vector3(dx, dy, dz);

        if (n.sqrMagnitude < 1e-8f)
            return Vector3.up;
        return n.normalized;

    }
}