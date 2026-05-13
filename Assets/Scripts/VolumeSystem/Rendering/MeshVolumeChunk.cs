using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshVolumeChunk : VolumeChunkBase
{
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;

    private readonly DualContouringOctreeMesher _mesher = new();
    private readonly DualContouringVoxelMesher _voxelMesher = new();

    /// <summary>Rebuilds this chunk by meshing only the grid edges owned by its bounds.</summary>
    public override void Rebuild(VolumeModel model, IScalarFieldSource source)
    {
        EnsureSetup();
        ApplySurfaceMaterial(model);

        _mesh.Clear();
        _mesh.indexFormat = IndexFormat.UInt32;

        switch (model.dataStructure)
        {
            case VolumeDataStructure.Octree:
                RebuildOctree(model, source);
                break;

            case VolumeDataStructure.VoxelGrid:
                RebuildVoxelGrid(model, source);
                break;
        }

        if (model.recalculateNormals)
            _mesh.RecalculateNormals();

        if (model.recalculateBounds)
            _mesh.RecalculateBounds();
    }

    private void RebuildOctree(VolumeModel model, IScalarFieldSource source)
    {
        OctreeVolume volume = model.octreeSampler.Volume;

        if (volume == null)
        {
            model.octreeSampler.RebuildVolume(source);
            volume = model.octreeSampler.Volume;
        }

        if (volume == null)
            return;

        _mesher.isoLevel = model.isoLevel;
        _mesher.ownedBounds = coreBounds;
        _mesher.BuildMesh(volume, _mesh);
        _mesher.ownedBounds = null;
    }

    private void RebuildVoxelGrid(VolumeModel model, IScalarFieldSource source)
    {
        VoxelGrid volume = model.voxelGridSampler.Volume;

        if (volume == null)
        {
            model.voxelGridSampler.RebuildVolume(source);
            volume = model.voxelGridSampler.Volume;
        }

        if (volume == null)
            return;

        _voxelMesher.ownedBounds = coreBounds;
        MeshData meshData = _voxelMesher.BuildMeshData(volume, model.isoLevel);
        _voxelMesher.ownedBounds = null;

        if (meshData == null)
            return;

        _mesh.SetVertices(meshData.Vertices);
        _mesh.SetTriangles(meshData.Triangles, 0);

        if (meshData.Bounds.size != Vector3.zero)
            _mesh.bounds = meshData.Bounds;
    }

    private void ApplySurfaceMaterial(VolumeModel model)
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_meshRenderer == null)
            return;

        if (model != null && model.surfaceMaterial != null)
        {
            _meshRenderer.sharedMaterial = model.surfaceMaterial;
            return;
        }

        if (_meshRenderer.sharedMaterial == null)
            _meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
    }

    /// <summary>Clears this chunk's generated mesh.</summary>
    public override void Clear()
    {
        EnsureSetup();

        if (_mesh != null)
            _mesh.Clear();
    }

    /// <summary>Initializes required components, mesh, and fallback material.</summary>
    private void EnsureSetup()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = $"Chunk Mesh {name}";
            _mesh.indexFormat = IndexFormat.UInt32;
        }

        if (_meshFilter.sharedMesh != _mesh)
            _meshFilter.sharedMesh = _mesh;

        if (_meshRenderer.sharedMaterial == null)
            _meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
    }

    /// <summary>Draws the chunk ownership bounds when the chunk is selected.</summary>
    private void OnDrawGizmosSelected()
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;

        Gizmos.matrix = transform.parent != null
            ? transform.parent.localToWorldMatrix
            : Matrix4x4.identity;

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.5f);
        Gizmos.DrawWireCube(coreBounds.center, coreBounds.size);

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.DrawWireCube(buildBounds.center, buildBounds.size);

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }
}
