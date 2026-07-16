using System.Collections.Generic;
using UnityEngine;

public class SceneCompositeSDF : IScalarFieldSource
{
    private readonly Matrix4x4 _rootLocalToWorld;
    private readonly CompiledObject[] _addObjects;
    private readonly CompiledObject[] _subtractObjects;
    private readonly CompiledObject[] _intersectObjects;

    private readonly struct CompiledObject
    {
        public readonly VolumeObject Object;
        public readonly Matrix4x4 WorldToLocal;

        public CompiledObject(VolumeObject obj)
        {
            Object = obj;
            WorldToLocal = obj.transform.worldToLocalMatrix;
        }

        public float Evaluate(Vector3 worldPoint)
        {
            return Object.EvaluateLocal(WorldToLocal.MultiplyPoint3x4(worldPoint));
        }
    }

    /// <summary>Creates a composed SDF from a snapshot of volume objects.</summary>
    public SceneCompositeSDF(Transform root, List<VolumeObject> objects)
    {
        _rootLocalToWorld = root.localToWorldMatrix;
        List<CompiledObject> addObjects = new();
        List<CompiledObject> subtractObjects = new();
        List<CompiledObject> intersectObjects = new();

        for (int i = 0; i < objects.Count; i++)
        {
            VolumeObject obj = objects[i];
            if (obj == null)
                continue;

            CompiledObject compiled = new CompiledObject(obj);
            switch (obj.role)
            {
                case VolumeOperationRole.Subtract:
                    subtractObjects.Add(compiled);
                    break;
                case VolumeOperationRole.Intersect:
                    intersectObjects.Add(compiled);
                    break;
                default:
                    addObjects.Add(compiled);
                    break;
            }
        }

        _addObjects = addObjects.ToArray();
        _subtractObjects = subtractObjects.ToArray();
        _intersectObjects = intersectObjects.ToArray();
    }

    /// <summary>Evaluates add, subtract, and intersect objects in composition order.</summary>
    public float Evaluate(Vector3 rootLocalPoint)
    {
        Vector3 worldPoint = _rootLocalToWorld.MultiplyPoint3x4(rootLocalPoint);
        float result = float.PositiveInfinity;

        for (int i = 0; i < _addObjects.Length; i++)
            result = Mathf.Min(result, _addObjects[i].Evaluate(worldPoint));

        for (int i = 0; i < _subtractObjects.Length; i++)
            result = Mathf.Max(result, -_subtractObjects[i].Evaluate(worldPoint));

        for (int i = 0; i < _intersectObjects.Length; i++)
            result = Mathf.Max(result, _intersectObjects[i].Evaluate(worldPoint));

        return result;
    }

    public int ObjectCount => _addObjects.Length + _subtractObjects.Length + _intersectObjects.Length;
}
