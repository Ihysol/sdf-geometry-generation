using System.Collections.Generic;
using UnityEngine;

public class SceneCompositeSDF : ISDF
{
    private readonly Transform _root;
    private readonly List<SDFObject> _objects;

    public SceneCompositeSDF(Transform root, List<SDFObject> objects)
    {
        _root = root;
        _objects = new List<SDFObject>(objects);
    }

    public float Evaluate(Vector3 rootLocalPoint)
    {
        float result = float.PositiveInfinity;

        foreach (var obj in _objects)
        {
            if (obj == null || obj.role != SDFOperationRole.Add)
                continue;

            float d = EvaluateObject(obj, rootLocalPoint);
            result = Mathf.Min(result, d);
        }

        foreach (var obj in _objects)
        {
            if (obj == null || obj.role != SDFOperationRole.Subtract)
                continue;

            float d = EvaluateObject(obj, rootLocalPoint);
            result = Mathf.Max(result, -d);
        }

        foreach (var obj in _objects)
        {
            if (obj == null || obj.role != SDFOperationRole.Intersect)
                continue;

            float d = EvaluateObject(obj, rootLocalPoint);
            result = Mathf.Max(result, d);
        }

        return result;
    }

    private float EvaluateObject(SDFObject obj, Vector3 rootLocalPoint)
    {
        Vector3 worldPoint = _root.TransformPoint(rootLocalPoint);
        Vector3 objectLocalPoint = obj.transform.InverseTransformPoint(worldPoint);

        return obj.EvaluateLocal(objectLocalPoint);
    }
}