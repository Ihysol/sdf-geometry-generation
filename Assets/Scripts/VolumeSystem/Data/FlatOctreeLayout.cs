using UnityEngine;

public sealed class FlatOctreeLayout
{
    public Vector3[] Centers;
    public Vector3[] Sizes;
    public int[] FirstChildIndex;
    public byte[] ChildMask;
    public byte[] Flags;
    public int Count => Centers != null ? Centers.Length : 0;

    public const byte FlagLeaf = 1 << 0;
    public const byte FlagSurface = 1 << 1;
}
