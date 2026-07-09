using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Renders GPU mesh data via procedural draw calls instead of Unity Mesh.</summary>
public class ProceduralDrawOutput : IVolumeOutput
{
    private Material _material;
    private GpuMeshData _meshData;
    private bool _hasMeshData;
    private Camera _camera;

    public ProceduralDrawOutput(Material material, Camera camera)
    {
        _material = material ?? new Material(Shader.Find("Standard"));
        _camera = camera;
        _hasMeshData = false;
    }

    /// <summary>Sets the render material (must support vertex positions + normals).</summary>
    public void SetMaterial(Material material)
    {
        _material = material;
    }

    /// <summary>Applies mesh data for subsequent procedural rendering.</summary>
    public void ApplyCpuMesh(CpuMeshData meshData)
    {
        Debug.LogWarning("ProceduralDrawOutput: Use BuildGpu() + ApplyGpuMesh() instead of CPU path.");
    }

    /// <summary>Stores GPU mesh data for rendering via DrawMeshInstancedProcedural.</summary>
    public void ApplyGpuMesh(GpuMeshData meshData)
    {
        if (_hasMeshData)
            _meshData.Dispose();

        _meshData = meshData;
        _hasMeshData = true;
    }

    /// <summary>Issues a procedural draw call for the stored GPU mesh.</summary>
    public void Render()
    {
        if (!_hasMeshData || _meshData.IndexCount == 0)
            return;

        if (_meshData.VertexBuffer == null || _meshData.IndexBuffer == null)
            return;

        if (_meshData.ArgsBuffer != null && SupportsDrawProceduralIndirect())
        {
            Graphics.DrawProceduralIndirect(
                _material,
                new Bounds(Vector3.zero, Vector3.one * 100f),
                0,
                _meshData.ArgsBuffer
            );
        }
        else
        {
            Mesh mesh = BuildFallbackMesh();
            if (mesh != null && _camera != null)
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            if (mesh != null)
                GameObject.DestroyImmediate(mesh);
        }
    }

    /// <summary>Renders with a custom world transform.</summary>
    public void Render(Matrix4x4 worldMatrix)
    {
        if (!_hasMeshData || _meshData.IndexCount == 0)
            return;

        if (_meshData.VertexBuffer == null || _meshData.IndexBuffer == null)
            return;

        if (_meshData.ArgsBuffer != null && SupportsDrawProceduralIndirect())
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            bounds.center = worldMatrix.MultiplyPoint(bounds.center);
            Graphics.DrawProceduralIndirect(
                _material,
                bounds,
                0,
                _meshData.ArgsBuffer
            );
        }
        else
        {
            Mesh mesh = BuildFallbackMesh();
            if (mesh != null)
                Graphics.DrawMeshNow(mesh, worldMatrix);
            if (mesh != null)
                GameObject.DestroyImmediate(mesh);
        }
    }

    /// <summary>Checks if the current platform supports DrawProceduralIndirect.</summary>
    private static bool SupportsDrawProceduralIndirect()
    {
        return SystemInfo.supportsGeometryShaders || SystemInfo.graphicsDeviceType >= GraphicsDeviceType.Direct3D11;
    }

    /// <summary>Builds a fallback Mesh from GraphicsBuffer data when indirect draw isn't available.</summary>
    private Mesh BuildFallbackMesh()
    {
        if (!_hasMeshData) return null;

        int vertexCount = _meshData.VertexCount;
        int indexCount = _meshData.IndexCount;

        if (vertexCount == 0 || indexCount == 0) return null;

        float[] vertData = new float[vertexCount * 10];
        _meshData.VertexBuffer.GetData(vertData);

        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            int idx = i * 10;
            vertices[i] = new Vector3(vertData[idx], vertData[idx + 1], vertData[idx + 2]);
            normals[i] = new Vector3(vertData[idx + 3], vertData[idx + 4], vertData[idx + 5]);
            uvs[i] = new Vector2(vertData[idx + 6], vertData[idx + 7]);
        }

        uint[] indicesU32 = new uint[indexCount];
        _meshData.IndexBuffer.GetData(indicesU32);

        int[] indices = new int[indexCount];
        for (int i = 0; i < indexCount; i++)
            indices[i] = (int)indicesU32[i];

        Mesh mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(indices, 0);

        return mesh;
    }

    public void Clear()
    {
        if (_hasMeshData)
        {
            _meshData.Dispose();
            _hasMeshData = false;
        }
    }
}
