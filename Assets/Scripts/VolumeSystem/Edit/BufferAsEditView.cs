using Unity.Collections;

/// <summary>Thin IVolumeView over a flat buffer — Seam 1 adapter (ADR-008).</summary>
public class BufferAsEditView : IVolumeView
{
    private readonly ChunkedFlatVolumeBuffer _buffer;

    public VolumeLayout Layout => _buffer.Layout;

    public BufferAsEditView(ChunkedFlatVolumeBuffer buffer)
    {
        _buffer = buffer;
    }

    public float GetDensity(int x, int y, int z)
    {
        var r = Layout.Resolution;
        if (x < 0 || x >= r.x || y < 0 || y >= r.y || z < 0 || z >= r.z)
            return float.MaxValue;
        return _buffer.DensityCpu[x + r.x * (y + r.y * z)];
    }

    public void SetDensity(int x, int y, int z, float value)
    {
        var r = Layout.Resolution;
        if (x < 0 || x >= r.x || y < 0 || y >= r.y || z < 0 || z >= r.z)
            return;
        _buffer.DensityCpu[x + r.x * (y + r.y * z)] = value;
    }
}
