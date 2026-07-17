using Unity.Collections;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ChunkRenderer : MonoBehaviour
{
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;

    public void ApplyMesh(CpuMeshData meshData)
    {
        EnsureSetup();

        // Mesher outputs world-space vertices. Transform to this chunk's local space.
        Matrix4x4 w2l = transform.worldToLocalMatrix;
        int vCount = meshData.VertexCount;
        int iCount = meshData.IndexCount;

        Vector3[] verts = new Vector3[vCount];
        for (int i = 0; i < vCount; i++)
            verts[i] = w2l.MultiplyPoint3x4(meshData.Vertices[i]);

        _mesh.Clear();
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.SetVertices(verts);

        int[] tris = new int[iCount];
        for (int i = 0; i < iCount; i++) tris[i] = meshData.Indices[i];
        _mesh.SetTriangles(tris, 0);

        // Normals: if per-vertex, transform as directions; otherwise recalculate
        NativeList<Vector3> normals = meshData.Normals;
        if (normals.IsCreated && normals.Length == vCount)
        {
            Vector3[] n = new Vector3[vCount];
            Matrix4x4 l2w = transform.localToWorldMatrix;
            for (int i = 0; i < vCount; i++)
                n[i] = l2w.MultiplyVector(normals[i]).normalized;
            _mesh.SetNormals(n);
        }
        else if (normals.IsCreated && normals.Length == iCount)
        {
            // Per-corner normals → accumulate to per-vertex
            Vector3[] accum = new Vector3[vCount];
            Matrix4x4 l2w = transform.localToWorldMatrix;
            for (int i = 0; i < iCount; i++)
                accum[tris[i]] += l2w.MultiplyVector(normals[i]);
            for (int i = 0; i < vCount; i++)
            {
                float len = accum[i].magnitude;
                accum[i] = len > 1e-8f ? accum[i] / len : Vector3.up;
            }
            _mesh.SetNormals(accum);
        }
        else
        {
            _mesh.RecalculateNormals();
        }

        if (meshData.UVs.IsCreated && meshData.UVs.Length == vCount)
        {
            Vector2[] uvs = new Vector2[vCount];
            for (int i = 0; i < vCount; i++) uvs[i] = meshData.UVs[i];
            _mesh.SetUVs(0, uvs);
        }

        Debug.Log($"[ChunkRenderer] {gameObject.name}: {vCount} verts, {iCount} indices");
    }

    public void SetMaterial(Material material)
    {
        EnsureSetup();
        if (_meshRenderer != null && material != null)
            _meshRenderer.sharedMaterial = material;
    }

    public void Clear()
    {
        EnsureSetup();
        _mesh.Clear();
    }

    private void EnsureSetup()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = $"Chunk{gameObject.name}";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        if (_meshFilter.sharedMesh != _mesh)
            _meshFilter.sharedMesh = _mesh;
    }
}
