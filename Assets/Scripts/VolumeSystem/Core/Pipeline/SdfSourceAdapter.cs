using UnityEngine;

public class SdfSourceAdapter : IVolumeSource
{
    private readonly IScalarFieldSource _source;

    public SdfSourceAdapter(IScalarFieldSource source)
    {
        _source = source;
    }

    public float Sample(Vector3 position)
    {
        return _source.Evaluate(position);
    }

    public int GetMaterial(Vector3 position)
    {
        return 0;
    }
}
