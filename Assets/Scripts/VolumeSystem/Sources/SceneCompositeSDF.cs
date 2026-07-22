using System.Collections.Generic;
using UnityEngine;

public class SceneCompositeSDF : IScalarFieldSource
{
    private readonly CompiledObject[] _addObjects;
    private readonly CompiledObject[] _subtractObjects;
    private readonly CompiledObject[] _intersectObjects;

    /// <summary>Zero-allocation: stores matrix columns as scalars so we can transform without Vector3 allocs.</summary>
    private readonly struct CompiledObject
    {
        public readonly VolumeObject Object;
        // worldToLocalMatrix stored as row-major 4x3 (m00..m23) — enough for point transform
        public readonly float M00, M01, M02, M03;
        public readonly float M10, M11, M12, M13;
        public readonly float M20, M21, M22, M23;

        public CompiledObject(VolumeObject obj)
        {
            Object = obj;
            Matrix4x4 m = obj.transform.worldToLocalMatrix;
            // Unity Matrix4x4 is column-major: m[row,col] → field mRxCx
            M00 = m.m00; M01 = m.m01; M02 = m.m02; M03 = m.m03;
            M10 = m.m10; M11 = m.m11; M12 = m.m12; M13 = m.m13;
            M20 = m.m20; M21 = m.m21; M22 = m.m22; M23 = m.m23;
        }

        public float Evaluate(float wx, float wy, float wz)
        {
            // Inline MultiplyPoint3x4 — no Vector3 allocation
            float lx = M00 * wx + M01 * wy + M02 * wz + M03;
            float ly = M10 * wx + M11 * wy + M12 * wz + M13;
            float lz = M20 * wx + M21 * wy + M22 * wz + M23;
            return Object.EvaluateLocal(lx, ly, lz);
        }

        public float Evaluate(Vector3 worldPoint)
        {
            return Evaluate(worldPoint.x, worldPoint.y, worldPoint.z);
        }
    }

    /// <summary>Creates a composed SDF from a snapshot of volume objects.</summary>
    public SceneCompositeSDF(Transform root, List<VolumeObject> objects)
    {
        List<CompiledObject> addObjects = new();
        List<CompiledObject> subtractObjects = new();
        List<CompiledObject> intersectObjects = new();

        for (int i = 0; i < objects.Count; i++)
        {
            VolumeObject obj = objects[i];
            if (obj == null) continue;

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
    public float Evaluate(Vector3 worldPoint)
    {
        float result = float.PositiveInfinity;

        for (int i = 0; i < _addObjects.Length; i++)
            result = Mathf.Min(result, _addObjects[i].Evaluate(worldPoint.x, worldPoint.y, worldPoint.z));

        for (int i = 0; i < _subtractObjects.Length; i++)
            result = Mathf.Max(result, -_subtractObjects[i].Evaluate(worldPoint.x, worldPoint.y, worldPoint.z));

        for (int i = 0; i < _intersectObjects.Length; i++)
            result = Mathf.Max(result, _intersectObjects[i].Evaluate(worldPoint.x, worldPoint.y, worldPoint.z));

        return result;
    }

    public int ObjectCount => _addObjects.Length + _subtractObjects.Length + _intersectObjects.Length;
}
