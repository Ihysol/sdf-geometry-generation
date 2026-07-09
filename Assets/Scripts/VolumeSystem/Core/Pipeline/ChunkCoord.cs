using System;

[System.Serializable]
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public ChunkCoord(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static ChunkCoord FromVector3Int(UnityEngine.Vector3Int v)
    {
        return new ChunkCoord(v.x, v.y, v.z);
    }

    public UnityEngine.Vector3Int ToVector3Int()
    {
        return new UnityEngine.Vector3Int(X, Y, Z);
    }

    public static ChunkCoord operator +(ChunkCoord a, ChunkCoord b)
    {
        return new ChunkCoord(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    public static bool operator ==(ChunkCoord a, ChunkCoord b)
    {
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }

    public static bool operator !=(ChunkCoord a, ChunkCoord b)
    {
        return !(a == b);
    }

    public bool Equals(ChunkCoord other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object obj)
    {
        return obj is ChunkCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + X;
            hash = hash * 31 + Y;
            hash = hash * 31 + Z;
            return hash;
        }
    }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z})";
    }
}
