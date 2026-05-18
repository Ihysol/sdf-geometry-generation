using System.Collections.Generic;
using UnityEngine;

public static class QefSolver
{
    public static bool TrySolve(
        List<Vector3> points,
        List<Vector3> normals,
        List<float> weights,
        Bounds clampBounds,
        out Vector3 solution)
    {
        solution = clampBounds.center;

        if (points == null || normals == null || points.Count == 0 || points.Count != normals.Count)
            return false;
        if (weights != null && weights.Count != points.Count)
            return false;

        float a00 = 0f, a01 = 0f, a02 = 0f;
        float a11 = 0f, a12 = 0f, a22 = 0f;
        float b0 = 0f, b1 = 0f, b2 = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 n = normals[i];
            float len = n.magnitude;

            if (len < 1e-8f)
                continue;

            n /= len;

            float w = 1f;
            if (weights != null)
                w = Mathf.Max(1e-4f, weights[i]);

            Vector3 p = points[i];
            float d = Vector3.Dot(n, p);

            a00 += w * n.x * n.x;
            a01 += w * n.x * n.y;
            a02 += w * n.x * n.z;
            a11 += w * n.y * n.y;
            a12 += w * n.y * n.z;
            a22 += w * n.z * n.z;

            b0 += w * n.x * d;
            b1 += w * n.y * d;
            b2 += w * n.z * d;
        }

        // Tikhonov regularization to stabilize near-singular systems.
        const float lambda = 1e-6f;
        a00 += lambda;
        a11 += lambda;
        a22 += lambda;

        if (!SolveSymmetric3x3(
                a00, a01, a02,
                a11, a12,
                a22,
                b0, b1, b2,
                out Vector3 x))
        {
            return false;
        }

        x.x = Mathf.Clamp(x.x, clampBounds.min.x, clampBounds.max.x);
        x.y = Mathf.Clamp(x.y, clampBounds.min.y, clampBounds.max.y);
        x.z = Mathf.Clamp(x.z, clampBounds.min.z, clampBounds.max.z);

        solution = x;
        return true;
    }

    public static bool TrySolve(
        List<Vector3> points,
        List<Vector3> normals,
        Bounds clampBounds,
        out Vector3 solution)
    {
        return TrySolve(points, normals, null, clampBounds, out solution);
    }

    private static bool SolveSymmetric3x3(
        float a00, float a01, float a02,
        float a11, float a12,
        float a22,
        float b0, float b1, float b2,
        out Vector3 x)
    {
        float[,] m = new float[3, 4]
        {
            { a00, a01, a02, b0 },
            { a01, a11, a12, b1 },
            { a02, a12, a22, b2 }
        };

        x = Vector3.zero;

        for (int col = 0; col < 3; col++)
        {
            int pivot = col;
            float maxAbs = Mathf.Abs(m[pivot, col]);

            for (int row = col + 1; row < 3; row++)
            {
                float v = Mathf.Abs(m[row, col]);
                if (v > maxAbs)
                {
                    maxAbs = v;
                    pivot = row;
                }
            }

            if (maxAbs < 1e-10f)
                return false;

            if (pivot != col)
            {
                for (int k = col; k < 4; k++)
                {
                    float tmp = m[col, k];
                    m[col, k] = m[pivot, k];
                    m[pivot, k] = tmp;
                }
            }

            float invPivot = 1f / m[col, col];
            for (int k = col; k < 4; k++)
                m[col, k] *= invPivot;

            for (int row = 0; row < 3; row++)
            {
                if (row == col)
                    continue;

                float factor = m[row, col];
                if (Mathf.Abs(factor) < 1e-12f)
                    continue;

                for (int k = col; k < 4; k++)
                    m[row, k] -= factor * m[col, k];
            }
        }

        x = new Vector3(m[0, 3], m[1, 3], m[2, 3]);
        return true;
    }
}
