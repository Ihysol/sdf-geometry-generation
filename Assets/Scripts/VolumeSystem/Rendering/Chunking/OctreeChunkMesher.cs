using UnityEngine;

public class OctreeChunkMesher : IChunkMesher<OctreeVolume>
{
    private readonly DualContouringOctreeMesher _mesher = new();

    public void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        OctreeVolume volume,
        Bounds coreBounds,
        Mesh targetMesh)
    {
        if (volume == null)
            return;

        _mesher.isoLevel = model.isoLevel;
        _mesher.useQefVertices = model.useQefVertices;
        _mesher.qefVertexMode = model.qefVertexMode;
        _mesher.qefBlendFactor = model.qefBlendFactor;
        _mesher.qefSnapEpsilon = model.qefSnapEpsilon;
        _mesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
        _mesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
        _mesher.qefEnableMultiHermite = model.qefEnableMultiHermite;
        _mesher.qefHermiteSamplesPerEdge = model.qefHermiteSamplesPerEdge;
        _mesher.ownedBounds = coreBounds;
        _mesher.BuildMesh(volume, model.isoLevel, targetMesh);
        _mesher.ownedBounds = null;
    }
}


