using UnityEngine;

public class UnityMeshOutput : IVolumeOutput
{
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private Material _material;

    public void SetMaterial(Material material)
    {
        _material = material;
        ApplyMaterial();
    }

    public UnityMeshOutput(MeshFilter meshFilter, MeshRenderer meshRenderer, Material material = null)
    {
        _meshFilter = meshFilter;
        _meshRenderer = meshRenderer;
        _material = material;
    }

    public UnityMeshOutput(Mesh mesh)
    {
        _mesh = mesh;
    }

    private void ApplyMaterial()
    {
        if (_meshRenderer != null && _material != null)
            _meshRenderer.sharedMaterial = _material;
    }

      public void ApplyCpuMesh(CpuMeshData meshData)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "VolumePipelineMesh";
        }

        meshData.ApplyTo(_mesh);

        if (_meshFilter != null)
            _meshFilter.sharedMesh = _mesh;

        ApplyMaterial();
    }

    public void ApplyCpuMesh(CpuMeshData meshData, Material material)
    {
        if (material != null)
            _material = material;
        ApplyCpuMesh(meshData);
    }

    public void ApplyGpuMesh(GpuMeshData meshData)
    {
    }

    public void Clear()
    {
        if (_mesh != null)
        {
            if (_meshFilter != null)
                _meshFilter.sharedMesh = null;
            Object.Destroy(_mesh);
            _mesh = null;
        }
    }
}
