using Unity.Collections;
using UnityEngine;

public struct CpuMeshData : System.IDisposable
{
    public NativeList<Vector3> Vertices;
    public NativeList<Vector3> Normals;
    public NativeList<Vector2> UVs;
    public NativeList<int> Indices;
    public NativeList<int> MaterialIds;

    private Allocator allocator;

    public CpuMeshData(Allocator alloc = Allocator.Persistent)
    {
        allocator = alloc;
        Vertices = new NativeList<Vector3>(alloc);
        Normals = new NativeList<Vector3>(alloc);
        UVs = new NativeList<Vector2>(alloc);
        Indices = new NativeList<int>(alloc);
        MaterialIds = new NativeList<int>(alloc);
    }

    public void Clear()
    {
        Vertices.Clear();
        Normals.Clear();
        UVs.Clear();
        Indices.Clear();
        MaterialIds.Clear();
    }

    public void Append(CpuMeshData other)
    {
        int vertOffset = Vertices.Length;
        int idxCount = Indices.Length;

        for (int i = 0; i < other.Vertices.Length; i++) Vertices.Add(other.Vertices[i]);
        for (int i = 0; i < other.Normals.Length; i++) Normals.Add(other.Normals[i]);
        for (int i = 0; i < other.UVs.Length; i++) UVs.Add(other.UVs[i]);
        for (int i = 0; i < other.Indices.Length; i++) Indices.Add(other.Indices[i] + vertOffset);
        for (int i = 0; i < other.MaterialIds.Length; i++) MaterialIds.Add(other.MaterialIds[i]);
    }

    public int VertexCount => Vertices.Length;
    public int IndexCount => Indices.Length;

    public void ApplyTo(Mesh mesh)
    {
        if (mesh == null) return;

        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

  Vector3[] verts = new Vector3[VertexCount];
        for (int i = 0; i < VertexCount; i++) verts[i] = Vertices[i];
        mesh.SetVertices(verts);

        int[] tris = new int[IndexCount];
        for (int i = 0; i < IndexCount; i++) tris[i] = Indices[i];
        mesh.SetTriangles(tris, 0);

        // Smooth face normals into vertex normals (when per-index normals are provided)
        if (Normals.Length == IndexCount)
        {
            Vector3[] vertexAccum = new Vector3[VertexCount];
            for (int i = 0; i < IndexCount; i++)
            {
                int vi = tris[i];
                vertexAccum[vi] += Normals[i];
            }
            Vector3[] norms = new Vector3[VertexCount];
            for (int i = 0; i < VertexCount; i++)
            {
                float len = vertexAccum[i].magnitude;
                norms[i] = len > 1e-8f ? vertexAccum[i] / len : Vector3.up;
            }
            mesh.SetNormals(norms);
        }
        else if (Normals.Length == VertexCount)
        {
            // Per-vertex normals from mesher – use as-is
            Vector3[] norms = new Vector3[VertexCount];
            for (int i = 0; i < VertexCount; i++) norms[i] = Normals[i];
            mesh.SetNormals(norms);
        }
        else
        {
            mesh.RecalculateNormals();
        }

        if (UVs.Length == VertexCount)
        {
            Vector2[] uvs = new Vector2[UVs.Length];
            for (int i = 0; i < UVs.Length; i++) uvs[i] = UVs[i];
            mesh.SetUVs(0, uvs);
        }
    }

    public Mesh ToMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Pipeline Generated Mesh";
        ApplyTo(mesh);
        return mesh;
    }

    public void Dispose()
    {
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Normals.IsCreated) Normals.Dispose();
        if (UVs.IsCreated) UVs.Dispose();
        if (Indices.IsCreated) Indices.Dispose();
        if (MaterialIds.IsCreated) MaterialIds.Dispose();
    }
}
