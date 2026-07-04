using UnityEngine;

public class UnityMeshOutput : IVolumeOutput
{
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    public Material Material
    {
        set
        {
            if (_meshRenderer != null)
                _meshRenderer.sharedMaterial = value;
        }
    }

    public UnityMeshOutput(MeshFilter meshFilter, MeshRenderer meshRenderer)
    {
        _meshFilter = meshFilter;
        _meshRenderer = meshRenderer;
    }

    public void ApplyCpuMesh(CpuMeshData meshData)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "VolumePipelineMesh";
            _meshFilter.sharedMesh = _mesh;
        }

        meshData.ApplyTo(_mesh);
    }

    public void ApplyGpuMesh(GpuMeshData meshData)
    {
    }

    public void Clear()
    {
        if (_mesh != null)
        {
            _meshFilter.sharedMesh = null;
            Object.Destroy(_mesh);
            _mesh = null;
        }
    }
}
