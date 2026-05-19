using UnityEngine;

public class OctreeChunkMesher : IChunkMesher<OctreeVolume>
{
    private readonly DualContouringOctreeMesher _mesher = new();
    private readonly DualMarchingCubesOctreeMesher _dualMarchingCubesMesher = new();
    private readonly DualMarchingTetrahedraOctreeMesher _dualMarchingTetrahedraMesher = new();

    public void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        OctreeVolume volume,
        Bounds coreBounds,
        Mesh targetMesh)
    {
        if (volume == null)
            return;

        switch (model.octreeMesherType)
        {
            case OctreeMesherType.DualMarchingCubes:
                _dualMarchingCubesMesher.ownedBounds = coreBounds;
                _dualMarchingCubesMesher.ownedBoundsList = null;
                _dualMarchingCubesMesher.BuildMesh(volume, model.isoLevel, targetMesh);
                _dualMarchingCubesMesher.ownedBounds = null;
                _dualMarchingCubesMesher.ownedBoundsList = null;
                break;
            case OctreeMesherType.DualMarchingTetrahedra:
                _dualMarchingTetrahedraMesher.ownedBounds = coreBounds;
                _dualMarchingTetrahedraMesher.ownedBoundsList = null;
                _dualMarchingTetrahedraMesher.BuildMesh(volume, model.isoLevel, targetMesh);
                _dualMarchingTetrahedraMesher.ownedBounds = null;
                _dualMarchingTetrahedraMesher.ownedBoundsList = null;
                break;

            case OctreeMesherType.DualContouring:
            default:
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
                break;
        }
    }
}


