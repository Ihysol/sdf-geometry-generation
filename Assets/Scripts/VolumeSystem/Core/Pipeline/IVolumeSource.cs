using UnityEngine;

public interface IVolumeSource
{
    float Sample(Vector3 position);
    float Sample(float x, float y, float z) => Sample(new Vector3(x, y, z));
    int GetMaterial(Vector3 position);
    int GetMaterial(float x, float y, float z) => GetMaterial(new Vector3(x, y, z));
}

public interface IAnalyticVolumeSource : IVolumeSource
{
    Vector3 GetNormal(Vector3 position);
}
