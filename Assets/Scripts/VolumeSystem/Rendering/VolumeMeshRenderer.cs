using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumeMeshRenderer : MonoBehaviour
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

    private readonly DualContouringVoxelMesher voxelMesher = new();
    private readonly DualContouringOctreeMesher octreeMesher = new();

    private void OnEnable()
    {
        EnsureSetup();
    }

    public void RebuildMesh(VolumeModel model)
    {
        EnsureSetup();

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
            meshFilter.sharedMesh = mesh;
        }

        if (meshRenderer.sharedMaterial == null)
        {
            meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
        }
    }
}