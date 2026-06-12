using UnityEngine;

public static class EdgeRefinementUtility
{
    public const float ResidualEpsilon = 1e-4f;

    public static bool ResidualIsAcceptable(float residual)
    {
        return Mathf.Abs(residual) <= ResidualEpsilon;
    }
}
