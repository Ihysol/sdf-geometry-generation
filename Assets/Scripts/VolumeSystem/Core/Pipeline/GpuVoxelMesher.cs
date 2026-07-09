using UnityEngine;

/// <summary>GPU-accelerated voxel surface mesher using compute shader.</summary>
public class GpuVoxelMesher : IVolumeMesher
{
    private ComputeShader _shader;

    private ComputeShader Shader
    {
        get
        {
            if (_shader == null)
                _shader = Resources.Load<ComputeShader>("GpuVoxelMesher");
            return _shader;
        }
    }

    public bool SupportsCpu => false;
    public bool SupportsGpu => true;

    public CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context)
    {
        throw new System.NotSupportedException("GpuVoxelMesher does not support CPU meshing.");
    }

    public GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context)
    {
        ComputeShader cs = Shader;
        if (cs == null || buffer.DensityCompute == null)
            return default;

        VolumeLayout layout = buffer.Layout;
        int totalCells = layout.TotalCells;

        // Estimate max vertices: each cell has up to 6 faces, each face has 4 vertices
        int maxVertices = totalCells * 6 * 4;
        int maxIndices = totalCells * 6 * 6;

        ComputeBuffer vertexBuffer = new ComputeBuffer(maxVertices, sizeof(float) * 10);
        ComputeBuffer indexBuffer = new ComputeBuffer(maxIndices, sizeof(uint));
        ComputeBuffer vertCounter = new ComputeBuffer(2, sizeof(uint));

        uint[] counters = new uint[] { 0, 0 };
        vertCounter.SetData(counters);

        cs.SetVector("_Resolution", new Vector4(layout.Resolution.x, layout.Resolution.y, layout.Resolution.z, 0f));
        cs.SetVector("_CellSizeOrigin", new Vector4(layout.CellSize, layout.Origin.x, layout.Origin.y, layout.Origin.z));
        cs.SetFloat("_IsoLevel", context.IsoLevel);

        int kernel = cs.FindKernel("CSMeshVoxels");
        cs.SetBuffer(kernel, "_DensityBuffer", buffer.DensityCompute);
        cs.SetBuffer(kernel, "_MaterialBuffer", buffer.MaterialCompute);
        cs.SetBuffer(kernel, "_VertexBuffer", vertexBuffer);
        cs.SetBuffer(kernel, "_IndexBuffer", indexBuffer);
        cs.SetBuffer(kernel, "_VertCounter", vertCounter);

        int groupsX = (layout.Resolution.x + 15) / 16;
        int groupsY = (layout.Resolution.y + 15) / 16;
        int groupsZ = Mathf.Max(1, layout.Resolution.z);

        cs.Dispatch(kernel, groupsX, groupsY, groupsZ);

        // Read back actual counts
        uint[] resultCounters = new uint[2];
        vertCounter.GetData(resultCounters);
        int vertexCount = (int)resultCounters[0];
        int indexCount = (int)resultCounters[1];

        // Convert to GraphicsBuffer for rendering
        GraphicsBuffer gpuVertexBuffer = CreateVertexBuffer(vertexBuffer, vertexCount);
        GraphicsBuffer gpuIndexBuffer = CreateIndexBuffer(indexBuffer, indexCount);

        // Args buffer: (indexCount, instanceCount, startVertexLocation, startInstanceLocation)
        GraphicsBuffer argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, sizeof(uint), 5);
        uint[] args = new uint[] { (uint)indexCount, 1u, 0u, 0u, 0u };
        argsBuffer.SetData(args);

        // Cleanup compute buffers
        vertexBuffer.Release();
        indexBuffer.Release();
        vertCounter.Release();

        return new GpuMeshData
        {
            VertexBuffer = gpuVertexBuffer,
            IndexBuffer = gpuIndexBuffer,
            ArgsBuffer = argsBuffer,
            VertexCount = vertexCount,
            IndexCount = indexCount
        };
    }

    private GraphicsBuffer CreateVertexBuffer(ComputeBuffer src, int count)
    {
        if (count == 0) return null;

        float[] data = new float[count * 10];
        src.GetData(data);

        GraphicsBuffer dst = new GraphicsBuffer(GraphicsBuffer.Target.Vertex, sizeof(float) * 10, count);
        dst.SetData(data);
        return dst;
    }

    private GraphicsBuffer CreateIndexBuffer(ComputeBuffer src, int count)
    {
        if (count == 0) return null;

        uint[] data = new uint[count];
        src.GetData(data);

        GraphicsBuffer dst = new GraphicsBuffer(GraphicsBuffer.Target.Index, sizeof(uint), count);
        dst.SetData(data);
        return dst;
    }
}
