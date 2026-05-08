using UnityEngine;

public interface IScalarFieldSource
{
    float Evaluate(Vector3 worldPosition);
}