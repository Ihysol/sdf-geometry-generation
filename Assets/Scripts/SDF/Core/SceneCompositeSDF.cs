using System.Collections.Generic;
using UnityEngine;

public class SceneCompositeSDF : IScalarFieldSource
{
    private readonly Transform _root;
    private readonly List<VolumeObject> _objects;

    public SceneCompositeSDF(Transform root, List<VolumeObject> objects)
    {
        _root = root;
        _objects = new List<VolumeObject>(objects);
    }

    public float Evaluate(Vector3 rootLocalPoint)
    {
        float result = float.PositiveInfinity;

        foreach (var obj in _objects)
        {
            if (obj == null || obj.role != VolumeOperationRole.Add)
                continue;

            float d = EvaluateObject(obj, rootLocalPoint);
            result = Mathf.Min(result, d);
        }

        foreach (var obj in _objects)
        {
            if (obj == null || obj.role != VolumeOperationRole.Subtract)
                continue;

            float d = EvaluateObject(obj, rootLocalPoint);
            result = Mathf.Max(result, -d);
        }

        foreach (var obj in _objects)
        {
            if (obj == null || obj.role != VolumeOperationRole.Intersect)
                continue;

            float d = EvaluateObject(obj, rootLocalPoint);
            result = Mathf.Max(result, d);
        }

        return result;
    }

    private float EvaluateObject(VolumeObject obj, Vector3 rootLocalPoint)
    {
        Vector3 worldPoint = _root.TransformPoint(rootLocalPoint);
        Vector3 objectLocalPoint = obj.transform.InverseTransformPoint(worldPoint);

        return obj.EvaluateLocal(objectLocalPoint);
    }
}