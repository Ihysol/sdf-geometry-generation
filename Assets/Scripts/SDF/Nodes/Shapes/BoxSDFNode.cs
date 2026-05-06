using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Box")]
public class BoxSDFNode : SDFNode
{
    public Vector3 halfExtents = Vector3.one;

    public override float Evaluate(Vector3 p)
    {
        Vector3 q = Abs(p) - halfExtents;
        return Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
    }

    private static Vector3 Abs(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}