using System.Collections.Generic;
using UnityEngine;

public static class QefSolver
{
    public enum RobustKernel
    {
        Cauchy,
        Huber,
        Tukey
    }

    public struct Settings
    {
        public int irlsIterations;
        public RobustKernel robustKernel;
        public float robustScale;
        public bool useAnisotropicRegularization;
        public float anisotropicStrength;
    }

    private static Settings DefaultSettings => new Settings
    {
        irlsIterations = 3,
        robustKernel = RobustKernel.Cauchy,
        robustScale = 2.5f,
        useAnisotropicRegularization = false,
        anisotropicStrength = 0.2f
    };

    public static bool TrySolve(
        List<Vector3> points,
        List<Vector3> normals,
        List<float> weights,
        Bounds clampBounds,
        out Vector3 solution)
    {
        return TrySolve(points, normals, weights, clampBounds, DefaultSettings, out solution);
    }

    public static bool TrySolve(
        List<Vector3> points,
        List<Vector3> normals,
        List<float> weights,
        Bounds clampBounds,
        Settings settings,
        out Vector3 solution)
    {
        solution = clampBounds.center;

        if (points == null || normals == null || points.Count == 0 || points.Count != normals.Count)
            return false;
        if (weights != null && weights.Count != points.Count)
            return false;

        float[] baseWeights = new float[points.Count];
        float[] robustWeights = new float[points.Count];

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 n = normals[i];
            float len = n.magnitude;

            if (len < 1e-8f)
            {
                baseWeights[i] = 0f;
                robustWeights[i] = 0f;
                continue;
            }

            float w = 1f;
            if (weights != null)
                w = Mathf.Max(1e-4f, weights[i]);
            baseWeights[i] = w;
            robustWeights[i] = 1f;
        }

        Vector3 x = clampBounds.center;
        bool solved = false;

        int iterations = Mathf.Max(1, settings.irlsIterations);
        for (int iter = 0; iter < iterations; iter++)
        {
            BuildNormalEquations(
                points,
                normals,
                baseWeights,
                robustWeights,
                out float a00,
                out float a01,
                out float a02,
                out float a11,
                out float a12,
                out float a22,
                out float b0,
                out float b1,
                out float b2,
                out float trace);

            // Adaptive Tikhonov regularization based on system scale.
            float lambda = Mathf.Max(1e-8f, trace * 1e-6f);
            a00 += lambda;
            a11 += lambda;
            a22 += lambda;
            if (settings.useAnisotropicRegularization)
            {
                AddAnisotropicDiagonal(
                    points,
                    normals,
                    baseWeights,
                    robustWeights,
                    trace,
                    Mathf.Max(0f, settings.anisotropicStrength),
                    ref a00,
                    ref a11,
                    ref a22);
            }

            solved = SolveSymmetric3x3(
                a00, a01, a02,
                a11, a12,
                a22,
                b0, b1, b2,
                out x);

            if (!solved)
                break;

            // Robust IRLS (Cauchy-like) based on current residuals.
            float sigma = EstimateResidualSigma(points, normals, x);
            if (sigma < 1e-8f)
                break;

            float c = Mathf.Max(1e-5f, sigma * Mathf.Max(0.1f, settings.robustScale));
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 n = normals[i];
                float len = n.magnitude;
                if (len < 1e-8f || baseWeights[i] <= 0f)
                {
                    robustWeights[i] = 0f;
                    continue;
                }

                n /= len;
                float r = Vector3.Dot(n, x - points[i]);
                robustWeights[i] = GetRobustWeight(settings.robustKernel, r, c);
            }
        }

        if (!solved)
        {
            return false;
        }

        x.x = Mathf.Clamp(x.x, clampBounds.min.x, clampBounds.max.x);
        x.y = Mathf.Clamp(x.y, clampBounds.min.y, clampBounds.max.y);
        x.z = Mathf.Clamp(x.z, clampBounds.min.z, clampBounds.max.z);

        solution = x;
        return true;
    }

    private static void BuildNormalEquations(
        List<Vector3> points,
        List<Vector3> normals,
        float[] baseWeights,
        float[] robustWeights,
        out float a00,
        out float a01,
        out float a02,
        out float a11,
        out float a12,
        out float a22,
        out float b0,
        out float b1,
        out float b2,
        out float trace)
    {
        a00 = a01 = a02 = 0f;
        a11 = a12 = a22 = 0f;
        b0 = b1 = b2 = 0f;
        trace = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 n = normals[i];
            float len = n.magnitude;
            if (len < 1e-8f)
                continue;

            n /= len;
            float w = Mathf.Max(0f, baseWeights[i] * robustWeights[i]);
            if (w <= 0f)
                continue;

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

        trace = a00 + a11 + a22;
    }

    private static float EstimateResidualSigma(List<Vector3> points, List<Vector3> normals, Vector3 x)
    {
        float sumAbs = 0f;
        int count = 0;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 n = normals[i];
            float len = n.magnitude;
            if (len < 1e-8f)
                continue;

            n /= len;
            float r = Mathf.Abs(Vector3.Dot(n, x - points[i]));
            sumAbs += r;
            count++;
        }

        if (count == 0)
            return 0f;

        return sumAbs / count;
    }

    public static bool TrySolve(
        List<Vector3> points,
        List<Vector3> normals,
        Bounds clampBounds,
        out Vector3 solution)
    {
        return TrySolve(points, normals, null, clampBounds, out solution);
    }

    private static float GetRobustWeight(RobustKernel kernel, float residual, float c)
    {
        float t = Mathf.Abs(residual) / Mathf.Max(1e-6f, c);
        switch (kernel)
        {
            case RobustKernel.Huber:
                return t <= 1f ? 1f : 1f / t;
            case RobustKernel.Tukey:
                if (t >= 1f) return 0f;
                float a = 1f - t * t;
                return a * a;
            case RobustKernel.Cauchy:
            default:
                return 1f / (1f + t * t);
        }
    }

    private static void AddAnisotropicDiagonal(
        List<Vector3> points,
        List<Vector3> normals,
        float[] baseWeights,
        float[] robustWeights,
        float trace,
        float anisotropicStrength,
        ref float a00,
        ref float a11,
        ref float a22)
    {
        if (anisotropicStrength <= 0f)
            return;

        float cxx = 0f;
        float cyy = 0f;
        float czz = 0f;
        float wsum = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 n = normals[i];
            float len = n.magnitude;
            if (len < 1e-8f)
                continue;

            n /= len;
            float w = Mathf.Max(0f, baseWeights[i] * robustWeights[i]);
            if (w <= 0f)
                continue;

            cxx += w * n.x * n.x;
            cyy += w * n.y * n.y;
            czz += w * n.z * n.z;
            wsum += w;
        }

        if (wsum <= 1e-8f)
            return;

        cxx /= wsum;
        cyy /= wsum;
        czz /= wsum;

        float invX = 1f / Mathf.Max(1e-6f, cxx);
        float invY = 1f / Mathf.Max(1e-6f, cyy);
        float invZ = 1f / Mathf.Max(1e-6f, czz);
        float norm = (invX + invY + invZ) / 3f;
        invX /= norm;
        invY /= norm;
        invZ /= norm;

        float k = Mathf.Max(1e-8f, trace * 1e-6f) * anisotropicStrength;
        a00 += k * invX;
        a11 += k * invY;
        a22 += k * invZ;
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
