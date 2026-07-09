using UnityEngine;

/// <summary>Dispatches volume operations via compute shader kernels.</summary>
public static class GpuOperationDispatcher
{
    private static ComputeShader _shader;

    private static ComputeShader Shader
    {
        get
        {
            if (_shader == null)
                _shader = Resources.Load<ComputeShader>("VolumeOperation");
            return _shader;
        }
    }

    private static void SetupVolumeParams(ComputeShader cs, VolumeLayout layout)
    {
        cs.SetVector("_Resolution", new Vector4(layout.Resolution.x, layout.Resolution.y, layout.Resolution.z, 0f));
        cs.SetVector("_CellSizeOrigin", new Vector4(layout.CellSize, layout.Origin.x, layout.Origin.y, layout.Origin.z));
        cs.SetInt("_TotalCells", layout.TotalCells);
    }

    public static void AddSphere(IVolumeBuffer buffer, Vector3 center, float radius, int materialId)
    {
        ComputeShader cs = Shader;
        if (cs == null || buffer.DensityCompute == null) return;

        SetupVolumeParams(cs, buffer.Layout);
        cs.SetVector("_Center", center);
        cs.SetFloat("_Radius", radius);
        cs.SetInt("_MaterialId", materialId);

        int kernel = cs.FindKernel("CSAddSphere");
        cs.SetBuffer(kernel, "_DensityBuffer", buffer.DensityCompute);
        cs.SetBuffer(kernel, "_MaterialBuffer", buffer.MaterialCompute);

        BoundsInt region = ComputeRegion(buffer.Layout, center, radius);
        Dispatch(cs, kernel, region);
    }

    public static void SubtractSphere(IVolumeBuffer buffer, Vector3 center, float radius)
    {
        ComputeShader cs = Shader;
        if (cs == null || buffer.DensityCompute == null) return;

        SetupVolumeParams(cs, buffer.Layout);
        cs.SetVector("_Center", center);
        cs.SetFloat("_Radius", radius);

        int kernel = cs.FindKernel("CSSubtractSphere");
        cs.SetBuffer(kernel, "_DensityBuffer", buffer.DensityCompute);
        cs.SetBuffer(kernel, "_MaterialBuffer", buffer.MaterialCompute);

        BoundsInt region = ComputeRegion(buffer.Layout, center, radius);
        Dispatch(cs, kernel, region);
    }

    public static void PaintMaterial(IVolumeBuffer buffer, Vector3 center, float radius, int materialId)
    {
        ComputeShader cs = Shader;
        if (cs == null || buffer.DensityCompute == null) return;

        SetupVolumeParams(cs, buffer.Layout);
        cs.SetVector("_Center", center);
        cs.SetFloat("_Radius", radius);
        cs.SetInt("_MaterialId", materialId);

        int kernel = cs.FindKernel("CSPaintMaterial");
        cs.SetBuffer(kernel, "_DensityBuffer", buffer.DensityCompute);
        cs.SetBuffer(kernel, "_MaterialBuffer", buffer.MaterialCompute);

        BoundsInt region = ComputeRegion(buffer.Layout, center, radius);
        Dispatch(cs, kernel, region);
    }

    public static void Smooth(IVolumeBuffer buffer, Vector3 center, float radius)
    {
        ComputeShader cs = Shader;
        if (cs == null || buffer.DensityCompute == null) return;

        SetupVolumeParams(cs, buffer.Layout);
        cs.SetVector("_Center", center);
        cs.SetFloat("_Radius", radius);

        int kernel = cs.FindKernel("CSSmooth");
        cs.SetBuffer(kernel, "_DensityBuffer", buffer.DensityCompute);

        // Temp ping-pong buffer
        ComputeBuffer tempBuffer = new ComputeBuffer(buffer.Layout.TotalCells, sizeof(float));
        cs.SetBuffer(kernel, "_TempDensityBuffer", tempBuffer);

        BoundsInt region = ComputeRegion(buffer.Layout, center, radius);
        Dispatch(cs, kernel, region);

        // Copy temp back to density
        buffer.SyncGpuToCpu();
        tempBuffer.Release();
    }

    private static void Dispatch(ComputeShader cs, int kernel, BoundsInt region)
    {
        int groupsX = (Mathf.Max(1, region.size.x) + 15) / 16;
        int groupsY = (Mathf.Max(1, region.size.y) + 15) / 16;
        int groupsZ = Mathf.Max(1, region.size.z);

        cs.Dispatch(kernel, groupsX, groupsY, groupsZ);
    }

    private static BoundsInt ComputeRegion(VolumeLayout layout, Vector3 center, float radius)
    {
        Vector3 minCell = (center - new Vector3(radius, radius, radius) - layout.Origin) / layout.CellSize;
        Vector3 maxCell = (center + new Vector3(radius, radius, radius) - layout.Origin) / layout.CellSize;

        int px = Mathf.FloorToInt(minCell.x);
        int py = Mathf.FloorToInt(minCell.y);
        int pz = Mathf.FloorToInt(minCell.z);
        int sx = Mathf.CeilToInt(maxCell.x) - px + 1;
        int sy = Mathf.CeilToInt(maxCell.y) - py + 1;
        int sz = Mathf.CeilToInt(maxCell.z) - pz + 1;

        return new BoundsInt(px, py, pz, sx, sy, sz);
    }
}
