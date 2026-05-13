using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumeMeshRenderer : MonoBehaviour, IVolumeRenderer
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

    private readonly DualContouringVoxelMesher voxelMesher = new();
    private readonly DualContouringOctreeMesher octreeMesher = new();

    /// <summary>Regenerates the single-mesh output for the model.</summary>
    public void Rebuild(VolumeModel model)
    {
        RebuildMesh(model);
    }

    /// <summary>Clears the generated mesh and detaches it from the mesh filter.</summary>
    public void Clear()
    {
        if (mesh != null)
            mesh.Clear();

        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (meshFilter != null)
            meshFilter.sharedMesh = null;
    }

    /// <summary>Builds the active volume data structure into one Unity mesh.</summary>
    public void RebuildMesh(VolumeModel model)
    {
        if (model.renderMode != VolumeRenderMode.SingleMesh)
        {
            Clear();
            enabled = false;
            return;
        }

        EnsureSetup();
        ApplySurfaceMaterial(model);

        // Wichtig: vor Clear/SetTriangles setzen
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.Clear();

        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                {
                    MeshData meshData = voxelMesher.BuildMeshData(
                        model.voxelGridSampler.Volume,
                        model.isoLevel
                    );

                    ApplyMeshData(meshData, model);
                    break;
                }

            case VolumeDataStructure.Octree:
                {
                    octreeMesher.isoLevel = model.isoLevel;
                    octreeMesher.BuildMesh(model.octreeSampler.Volume, mesh);

                    break;
                }
        }

        Debug.Log($"VolumeMeshRenderer: vertex count = {mesh.vertexCount}, indexFormat = {mesh.indexFormat}");
    }

    private void ApplySurfaceMaterial(VolumeModel model)
    {
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer == null)
            return;

        if (model != null && model.surfaceMaterial != null)
        {
            meshRenderer.sharedMaterial = model.surfaceMaterial;
            return;
        }

        if (meshRenderer.sharedMaterial == null)
            meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
    }

    /// <summary>Copies generated mesh buffers into the Unity mesh.</summary>
    private void ApplyMeshData(MeshData meshData, VolumeModel model)
    {
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.Clear();

        if (meshData == null)
            return;

        mesh.SetVertices(meshData.Vertices);
        mesh.SetTriangles(meshData.Triangles, 0);

        if (meshData.Bounds.size != Vector3.zero)
            mesh.bounds = meshData.Bounds;

        if (model.recalculateNormals)
            mesh.RecalculateNormals();

        if (model.recalculateBounds)
            mesh.RecalculateBounds();
    }

    /// <summary>Initializes required components, mesh, and fallback material.</summary>
    private void EnsureSetup()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        if (mesh == null || mesh.indexFormat != IndexFormat.UInt32)
        {
            mesh = new Mesh();
            mesh.name = "Volume Mesh";
            mesh.indexFormat = IndexFormat.UInt32;
        }

        // WICHTIG:
        // Nach Clear() kann sharedMesh null sein.
        // Deshalb immer wieder zuweisen.
        if (meshFilter.sharedMesh != mesh)
            meshFilter.sharedMesh = mesh;

        if (meshRenderer.sharedMaterial == null)
            meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
    }
}
