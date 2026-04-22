using UnityEngine;

public struct SDFSample
{
    public Vector3 LocalPosition;
    public float Distance;

    public bool isInside => Distance < 0f;
}