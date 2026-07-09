using UnityEngine;

/// <summary>Runtime instance of a placed feature in the volume.</summary>
[System.Serializable]
public class FeatureInstance
{
    public FeatureDefinition Definition { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Scale { get; set; }
    public int MaterialId { get; set; }

    public FeatureInstance(FeatureDefinition definition, Vector3 position)
    {
        Definition = definition;
        Position = position;
        Rotation = Quaternion.identity;
        Scale = Vector3.one;
        MaterialId = definition.defaultMaterialId;
    }

    /// <summary>Evaluates the feature SDF at a world-space point.</summary>
    public float Evaluate(Vector3 p)
    {
        if (Definition == null) return 1f;

        Vector3 local = InverseTransform(p);
        return Definition.EvaluateLocal(local);
    }

    /// <summary>Returns the affected world-space bounds for this instance.</summary>
    public Bounds GetBounds()
    {
        Vector3 halfExtents = Definition.GetApproximateHalfExtents();
        Vector3 scaled = new Vector3(
            Mathf.Abs(halfExtents.x * Scale.x),
            Mathf.Abs(halfExtents.y * Scale.y),
            Mathf.Abs(halfExtents.z * Scale.z)
        );
        return new Bounds(Position, scaled * 2f);
    }

    private Vector3 InverseTransform(Vector3 p)
    {
        Vector3 translated = p - Position;
        Vector3 rotated = Quaternion.Inverse(Rotation) * translated;
        return new Vector3(
            rotated.x / Scale.x,
            rotated.y / Scale.y,
            rotated.z / Scale.z
        );
    }
}
