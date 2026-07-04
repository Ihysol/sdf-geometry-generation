using UnityEngine;

public struct GpuMeshData : System.IDisposable
{
    public GraphicsBuffer VertexBuffer;
    public GraphicsBuffer IndexBuffer;
    public GraphicsBuffer ArgsBuffer;
    public int VertexCount;
    public int IndexCount;

    public void Dispose()
    {
        if (VertexBuffer != null) { VertexBuffer.Release(); VertexBuffer = null; }
        if (IndexBuffer != null) { IndexBuffer.Release(); IndexBuffer = null; }
        if (ArgsBuffer != null) { ArgsBuffer.Release(); ArgsBuffer = null; }
        VertexCount = 0;
        IndexCount = 0;
    }
}
