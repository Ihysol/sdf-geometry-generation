using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshVolumeChunk : VolumeChunkBase
{
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;

    private readonly OctreeChunkMesher _octreeChunkMesher = new();
    private readonly VoxelGridChunkMesher _voxelGridChunkMesher = new();

    /// <summary>Rebuilds this chunk by meshing only the grid edges owned by its bounds.</summary>
    public override void Rebuild(VolumeModel model, IScalarFieldSource source)
    {
        Rebuild(model, source, null);
    }

    public void ApplyMeshData(MeshData meshData, VolumeModel model)
    {
        EnsureSetup();

        _mesh.Clear();
        _mesh.indexFormat = IndexFormat.UInt32;

        if (meshData != null)
            meshData.ApplyTo(_mesh);

        if (model != null && model.recalculateNormals && !_mesh.HasVertexAttribute(VertexAttribute.Normal))
        {
            _mesh.RecalculateNormals();
        }

        if (model != null && model.recalculateBounds)
        {
            _mesh.bounds = coreBounds;
        }
    }

    public void Rebuild(VolumeModel model, IScalarFieldSource source, OctreeChunkMesher sharedOctreeChunkMesher)
    {
        EnsureSetup();

        _mesh.Clear();
        _mesh.indexFormat = IndexFormat.UInt32;

        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                {
                    VoxelGrid volume = model.voxelGridSampler.Volume;
                    if (volume == null)
                    {
                        model.voxelGridSampler.RebuildVolume(source);
                        volume = model.voxelGridSampler.Volume;
                    }

                    if (volume != null)
                    {
                        _voxelGridChunkMesher.BuildChunk(
                            model,
                            source,
                            volume,
                            coreBounds,
                            _mesh
                        );
                    }
                    break;
                }

            case VolumeDataStructure.Octree:
            case VolumeDataStructure.SparseVoxelOctree:
                {
                    OctreeVolume volume = model.GetActiveOctreeVolume();
                    if (volume == null)
                    {
                        if (model.dataStructure == VolumeDataStructure.Octree)
                            model.octreeSampler.RebuildVolume(source);
                        else
                            model.sparseVoxelOctreeSampler.RebuildVolume(source);
                        volume = model.GetActiveOctreeVolume();
                    }

                    if (volume != null)
                    {
                        (sharedOctreeChunkMesher ?? _octreeChunkMesher).BuildChunk(
                            model,
                            source,
                            volume,
                            coreBounds,
                            _mesh
                        );
                    }
                    break;
                }
        }

        if (model.recalculateNormals && !_mesh.HasVertexAttribute(VertexAttribute.Normal))
        {
            _mesh.RecalculateNormals();
        }

        if (model.recalculateBounds)
        {
            _mesh.bounds = coreBounds;
        }
    }

    public void SetSurfaceMaterial(Material material)
    {
        EnsureSetup();
        ApplyMaterial(material);
    }

    private void ApplyMaterial(Material material)
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_meshRenderer == null)
            return;

        if (material != null)
        {
            _meshRenderer.sharedMaterial = material;
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
    }

    /// <summary>Draws chunk ownership bounds when enabled in the parent model.</summary>
    private void OnDrawGizmos()
    {
        if (!ShouldDrawChunkGizmos(alwaysOnly: true))
            return;

        DrawChunkGizmos();
    }

    /// <summary>Draws chunk ownership bounds when selected.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!ShouldDrawChunkGizmos(alwaysOnly: false))
            return;

        DrawChunkGizmos();
    }

    private bool ShouldDrawChunkGizmos(bool alwaysOnly)
    {
        VolumeModel model = GetComponentInParent<VolumeModel>();

        if (model == null)
            return !alwaysOnly;

        if (!model.drawChildGizmos)
            return false;

        if (alwaysOnly)
            return model.drawChunkGizmosAlways;

        return true;
    }

    private void DrawChunkGizmos()
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
