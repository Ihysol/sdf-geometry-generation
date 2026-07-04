using UnityEngine;

public interface IVolumeSource
{
    float Sample(Vector3 position);
    int GetMaterial(Vector3 position);
}

public interface IAnalyticVolumeSource : IVolumeSource
{
    Vector3 GetNormal(Vector3 position);
}
