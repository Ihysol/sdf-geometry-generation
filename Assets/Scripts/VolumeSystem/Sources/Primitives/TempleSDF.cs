using UnityEngine;
[CreateAssetMenu(menuName = "SDF/Primitives/Temple")]

public class TempleSDF : SDF
{
    [Header("Global Scale")]
    public float scale = 1f;

    /// <summary>Evaluates the complete temple SDF in local space.</summary>
    public override float Evaluate(Vector3 p)
    {

        p /= scale;

        float d = 9999f;

        // ===== Base platform =====
        d = Union(d, Box(p - new Vector3(0, 0.15f, 0), new Vector3(3.2f, 0.15f, 3.8f)));
        d = Union(d, Box(p - new Vector3(0, 0.38f, 0), new Vector3(2.8f, 0.12f, 3.4f)));
        d = Union(d, Box(p - new Vector3(0, 0.58f, 0), new Vector3(2.45f, 0.10f, 3.05f)));

        // ===== Front stairs =====
        d = Union(d, Box(p - new Vector3(0, 0.08f, -4.05f), new Vector3(2.8f, 0.08f, 0.35f)));
        d = Union(d, Box(p - new Vector3(0, 0.22f, -3.75f), new Vector3(2.6f, 0.08f, 0.30f)));
        d = Union(d, Box(p - new Vector3(0, 0.36f, -3.48f), new Vector3(2.4f, 0.08f, 0.25f)));

        // ===== Cell / inner building body =====
        Vector3 cellP = p - new Vector3(0, 1.45f, 0.25f);
        float cellOuter = Box(cellP, new Vector3(1.55f, 0.95f, 2.15f));

        // Door cutout at front
        float door = Box(p - new Vector3(0, 1.05f, -1.95f), new Vector3(0.45f, 0.75f, 0.18f));
        cellOuter = Subtract(cellOuter, door);

        d = Union(d, cellOuter);

        // ===== Columns =====
        float columnY = 1.35f;
        float columnHeight = 1.75f;
        float columnRadius = 0.13f;

        // front row
        for (int i = 0; i < 6; i++)
        {
            float x = Mathf.Lerp(-2.0f, 2.0f, i / 5f);
            d = Union(d, Column(p, new Vector3(x, columnY, -2.45f), columnRadius, columnHeight));
        }

        // back row
        for (int i = 0; i < 6; i++)
        {
            float x = Mathf.Lerp(-2.0f, 2.0f, i / 5f);
            d = Union(d, Column(p, new Vector3(x, columnY, 2.45f), columnRadius, columnHeight));
        }

        // side rows
        for (int i = 1; i < 5; i++)
        {
            float z = Mathf.Lerp(-1.65f, 1.65f, i / 5f);
            d = Union(d, Column(p, new Vector3(-2.15f, columnY, z), columnRadius, columnHeight));
            d = Union(d, Column(p, new Vector3(2.15f, columnY, z), columnRadius, columnHeight));
        }

        // ===== Architrave / roof base =====
        d = Union(d, Box(p - new Vector3(0, 2.35f, 0), new Vector3(2.65f, 0.16f, 3.0f)));
        d = Union(d, Box(p - new Vector3(0, 2.58f, 0), new Vector3(2.85f, 0.12f, 3.15f)));

        // ===== Sloped roof =====
        d = Union(d, TempleRoof(p - new Vector3(0, 2.75f, 0), 3.05f, 3.35f, 0.75f));

        // ===== Front and back triangular pediments =====
        d = Union(d, Pediment(p - new Vector3(0, 2.63f, -3.22f), 2.75f, 0.75f, 0.16f));
        d = Union(d, Pediment(p - new Vector3(0, 2.63f, 3.22f), 2.75f, 0.75f, 0.16f));

        // ===== Decorative horizontal grooves =====
        d = Subtract(d, GrooveBand(p, 0.72f));
        d = Subtract(d, GrooveBand(p, 2.28f));
        d = Subtract(d, GrooveBand(p, 2.55f));

        return d;
    }

    // =========================
    // Temple parts
    // =========================

    /// <summary>Evaluates a fluted column with base and capital rings.</summary>
    private float Column(Vector3 p, Vector3 center, float r, float h)
    {
        Vector3 q = p - center;

        float shaft = Cylinder(q, r, h);

        // slight ring details
        float base1 = Cylinder(q - new Vector3(0, -h * 0.5f - 0.05f, 0), r * 1.45f, 0.10f);
        float base2 = Cylinder(q - new Vector3(0, -h * 0.5f + 0.08f, 0), r * 1.25f, 0.08f);

        float cap1 = Cylinder(q - new Vector3(0, h * 0.5f + 0.05f, 0), r * 1.45f, 0.10f);
        float cap2 = Cylinder(q - new Vector3(0, h * 0.5f - 0.08f, 0), r * 1.25f, 0.08f);

        // vertical fluting grooves
        float grooves = FlutedColumnCut(q, r, h);

        float col = Union(shaft, base1);
        col = Union(col, base2);
        col = Union(col, cap1);
        col = Union(col, cap2);

        col = Subtract(col, grooves);

        return col;
    }

    /// <summary>Evaluates the gabled roof prism.</summary>
    private float TempleRoof(Vector3 p, float halfWidth, float halfDepth, float roofHeight)
    {
        // Prism-like gable roof along Z
        Vector3 q = p;

        float dx = Mathf.Abs(q.x);
        float roofLine = roofHeight * (1f - dx / halfWidth);

        float side = q.y - roofLine;
        float bottom = -q.y;
        float zLimit = Mathf.Abs(q.z) - halfDepth;

        return Mathf.Max(Mathf.Max(side, bottom), zLimit);
    }

    /// <summary>Evaluates a triangular front or back pediment.</summary>
    private float Pediment(Vector3 p, float halfWidth, float height, float thickness)
    {
        Vector3 q = p;

        float dx = Mathf.Abs(q.x);
        float triangleTop = height * (1f - dx / halfWidth);

        float insideTri = Mathf.Max(q.y - triangleTop, -q.y);
        float widthLimit = dx - halfWidth;
        float depthLimit = Mathf.Abs(q.z) - thickness;

        return Mathf.Max(Mathf.Max(insideTri, widthLimit), depthLimit);
    }

    /// <summary>Evaluates a horizontal groove cutter band.</summary>
    private float GrooveBand(Vector3 p, float y)
    {
        Vector3 q = p - new Vector3(0, y, 0);
        return Box(q, new Vector3(3.0f, 0.025f, 3.3f));
    }

    /// <summary>Evaluates vertical groove cutters around a column shaft.</summary>
    private float FlutedColumnCut(Vector3 p, float radius, float height)
    {
        float angle = Mathf.Atan2(p.z, p.x);
        float radial = new Vector2(p.x, p.z).magnitude;

        int grooveCount = 16;
        float sector = Mathf.PI * 2f / grooveCount;

        float localAngle = Repeat(angle, sector);
        float grooveArc = Mathf.Abs(localAngle) * radius;

        float groove = Mathf.Max(
            grooveArc - 0.025f,
            Mathf.Abs(radial - radius) - 0.035f
        );

        float yLimit = Mathf.Abs(p.y) - height * 0.45f;

        return Mathf.Max(groove, yLimit);
    }

    // =========================
    // SDF primitives
    // =========================

    /// <summary>Evaluates an axis-aligned box SDF.</summary>
    private float Box(Vector3 p, Vector3 b)
    {
        Vector3 q = Abs(p) - b;
        return LengthMax(q) + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
    }

    /// <summary>Evaluates a capped cylinder SDF aligned to the Y axis.</summary>
    private float Cylinder(Vector3 p, float r, float h)
    {
        Vector2 d = new Vector2(
            new Vector2(p.x, p.z).magnitude - r,
            Mathf.Abs(p.y) - h * 0.5f
        );

        return Mathf.Min(Mathf.Max(d.x, d.y), 0f) + new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude;
    }

    // =========================
    // SDF operations
    // =========================

    /// <summary>Combines two SDFs with a hard union.</summary>
    private float Union(float a, float b)
    {
        return Mathf.Min(a, b);
    }

    /// <summary>Subtracts one SDF from another.</summary>
    private float Subtract(float a, float b)
    {
        return Mathf.Max(a, -b);
    }

    // =========================
    // Helpers
    // =========================

    /// <summary>Returns the component-wise absolute value.</summary>
    private Vector3 Abs(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    /// <summary>Returns the length of the positive part of a vector.</summary>
    private float LengthMax(Vector3 v)
    {
        return new Vector3(
            Mathf.Max(v.x, 0f),
            Mathf.Max(v.y, 0f),
            Mathf.Max(v.z, 0f)
        ).magnitude;
    }

    /// <summary>Repeats a coordinate around zero for radial groove placement.</summary>
    private float Repeat(float x, float period)
    {
        return x - period * Mathf.Round(x / period);
    }
}
