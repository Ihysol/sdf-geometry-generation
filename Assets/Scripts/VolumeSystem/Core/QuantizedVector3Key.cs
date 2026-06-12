using System;
using UnityEngine;

public readonly struct QuantizedVector3Key : IEquatable<QuantizedVector3Key>
{
    public readonly long X;
    public readonly long Y;
    public readonly long Z;

    public QuantizedVector3Key(long x, long y, long z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static float GetQuantum(Vector3 cellSize)
    {
        float minCell = MinPositive(Mathf.Abs(cellSize.x), Mathf.Abs(cellSize.y), Mathf.Abs(cellSize.z));
        return Mathf.Max(minCell * 1e-4f, 1e-6f);
    }

    public static QuantizedVector3Key FromPosition(Vector3 position, Vector3 origin, float quantum)
    {
        float q = Mathf.Max(quantum, 1e-6f);
        return new QuantizedVector3Key(
            Quantize((position.x - origin.x) / q),
            Quantize((position.y - origin.y) / q),
            Quantize((position.z - origin.z) / q)
        );
    }

    public bool Equals(QuantizedVector3Key other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object obj)
    {
        return obj is QuantizedVector3Key other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = X.GetHashCode();
            hash = (hash * 397) ^ Y.GetHashCode();
            hash = (hash * 397) ^ Z.GetHashCode();
            return hash;
        }
    }

    private static long Quantize(float value)
    {
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static float MinPositive(float a, float b, float c)
    {
        float min = float.PositiveInfinity;
        if (a > 0f) min = Mathf.Min(min, a);
        if (b > 0f) min = Mathf.Min(min, b);
        if (c > 0f) min = Mathf.Min(min, c);
        return float.IsPositiveInfinity(min) ? 1f : min;
    }
}
