using Unity.Collections;
using UnityEngine;

/// <summary>Thin IVolumeView over a flat buffer — Seam 1 adapter (ADR-008).
/// Caches NativeArray reference for bulk operations. Short-lived; do not hold across frames.</summary>
public class BufferAsEditView : IVolumeView
{
    private readonly ChunkedFlatVolumeBuffer _buffer;
    private NativeArray<float> _density;
    private readonly Vector3Int _res;
    private readonly int _xY; // res.x * res.y for indexing

    public VolumeLayout Layout => _buffer.Layout;

    public BufferAsEditView(ChunkedFlatVolumeBuffer buffer)
    {
        _buffer = buffer;
        _density = buffer.DensityCpu; // Borrowed reference — safe while buffer lives
        _res = buffer.Layout.Resolution;
        _xY = _res.x * _res.y;
    }

    public float GetDensity(int x, int y, int z)
    {
        if (x < 0 || x >= _res.x || y < 0 || y >= _res.y || z < 0 || z >= _res.z)
            return float.MaxValue;
        return _density[x + _res.x * y + _xY * z];
    }

    public void SetDensity(int x, int y, int z, float value)
    {
        if (x < 0 || x >= _res.x || y < 0 || y >= _res.y || z < 0 || z >= _res.z)
            return;
        _density[x + _res.x * y + _xY * z] = value;
    }
}
