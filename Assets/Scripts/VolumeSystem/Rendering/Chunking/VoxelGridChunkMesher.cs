using UnityEngine;

public class VoxelGridChunkMesher : IChunkMesher
{
    private readonly DualContouringVoxelMesher _mesher = new();

    public bool CanHandle(VolumeModel model, IVolumeData activeVolume)
    {
        return model != null && model.dataStructure == VolumeDataStructure.VoxelGrid;
    }

    public void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        Bounds coreBounds,
        Mesh targetMesh)
    {
        VoxelGrid volume = model.voxelGridSampler.Volume;

        if (volume == null)
        {
            model.voxelGridSampler.RebuildVolume(source);
            volume = model.voxelGridSampler.Volume;
        }

        if (volume == null)
            return;

        _mesher.ownedBounds = coreBounds;
        MeshData meshData = _mesher.BuildMeshData(volume, model.isoLevel);
        _mesher.ownedBounds = null;

        if (meshData == null)
            return;

        targetMesh.SetVertices(meshData.Vertices);
        targetMesh.SetTriangles(meshData.Triangles, 0);

        if (meshData.Bounds.size != Vector3.zero)
            targetMesh.bounds = meshData.Bounds;
    }
}
