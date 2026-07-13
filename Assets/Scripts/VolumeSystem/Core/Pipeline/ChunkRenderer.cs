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
        Debug.Log($"[ChunkRenderer] Applying mesh to {gameObject.name}: {meshData.VertexCount} verts, {meshData.IndexCount} indices");
        meshData.ApplyTo(_mesh);
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
