using UnityEngine;

public class OctreeChunkMesher : IChunkMesher
{
    private readonly DualContouringOctreeMesher _mesher = new();

    public bool CanHandle(VolumeModel model, IVolumeData activeVolume)
    {
        return model != null && model.dataStructure == VolumeDataStructure.Octree;
    }

    public void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        Bounds coreBounds,
        Mesh targetMesh)
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
        _mesher.useQefVertices = model.useQefVertices;
        _mesher.qefVertexMode = model.qefVertexMode;
        _mesher.qefBlendFactor = model.qefBlendFactor;
        _mesher.qefSnapEpsilon = model.qefSnapEpsilon;
        _mesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
        _mesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
        _mesher.ownedBounds = coreBounds;
        _mesher.BuildMesh(volume, targetMesh);
        _mesher.ownedBounds = null;
    }
}


