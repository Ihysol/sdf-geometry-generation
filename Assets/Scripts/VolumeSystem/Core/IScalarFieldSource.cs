using UnityEngine;

public interface IScalarFieldSource
{
    /// <summary>Samples the signed distance field at a world-space position.</summary>
    float Evaluate(Vector3 worldPosition);
}
