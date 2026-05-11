using UnityEngine;

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

        mesh.Clear();

        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                {
                    MeshData meshData = voxelMesher.BuildMeshData(
                        model.voxelGridSampler.Volume,
                        model.isoLevel
                    );
                    ApplyMeshData(meshData);

                    break;
                }

            case VolumeDataStructure.Octree:
                {
                    octreeMesher.BuildMesh(
                        model.octreeSampler.Volume,
                        mesh
                    );

                    break;
                }
        }

        Debug.Log($"VolumeMeshRenderer: vertex count = {mesh.vertexCount}");
    }

    private void EnsureSetup()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "Volume Mesh";

            meshFilter.sharedMesh = mesh;
        }

        if (meshRenderer.sharedMaterial == null)
        {
            meshRenderer.sharedMaterial =
                new Material(Shader.Find("Standard"));
        }
    }

    private void ApplyMeshData(MeshData meshData)
    {
        mesh.Clear();

        if (meshData == null)
            return;

        mesh.SetVertices(meshData.Vertices);
        mesh.SetTriangles(meshData.Triangles, 0);

        if (meshData.Bounds.size != Vector3.zero)
            mesh.bounds = meshData.Bounds;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}