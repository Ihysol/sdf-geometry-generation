using UnityEngine;

public class OctreeChunkMesher : IChunkMesher<OctreeVolume>
{
    private readonly DualContouringOctreeMesher _mesher = new();
    private readonly DualContouringFlatOctreeMesher _flatMesher = new();
    private readonly DualMarchingCubesOctreeMesher _dualMarchingCubesMesher = new();
    private readonly DualMarchingTetrahedraOctreeMesher _dualMarchingTetrahedraMesher = new();
    private readonly SurfaceNetsOctreeMesher _surfaceNetsMesher = new();

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
            case OctreeMesherType.SurfaceNets:
                _surfaceNetsMesher.ownedBounds = coreBounds;
                _surfaceNetsMesher.ownedBoundsList = null;
                _surfaceNetsMesher.BuildMesh(volume, model.isoLevel, targetMesh);
                _surfaceNetsMesher.ownedBounds = null;
                _surfaceNetsMesher.ownedBoundsList = null;
                break;

            case OctreeMesherType.DualContouring:
            default:
                if (model.storageMode == VolumeStorageMode.Flat)
                {
                    IFlatAdaptiveVolumeData flatVolume = model.GetActiveFlatAdaptiveVolume() ?? volume;
                    _flatMesher.enableDebugLog = model != null && model.ShouldLogChunkRebuildStats();
                    _flatMesher.isoLevel = model.isoLevel;
                    _flatMesher.useQefVertices = model.useQefVertices;
                    _flatMesher.qefVertexMode = model.qefVertexMode;
                    _flatMesher.qefBlendFactor = model.qefBlendFactor;
                    _flatMesher.qefSnapEpsilon = model.qefSnapEpsilon;
                    _flatMesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
                    _flatMesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
                    _flatMesher.qefEnableMultiHermite = model.qefEnableMultiHermite;
                    _flatMesher.qefHermiteSamplesPerEdge = model.qefHermiteSamplesPerEdge;
                    _flatMesher.ownedBounds = coreBounds;
                    _flatMesher.ownedBoundsList = null;
                    _flatMesher.BuildMesh(flatVolume, model.isoLevel, targetMesh);
                    _flatMesher.ownedBounds = null;
                    _flatMesher.ownedBoundsList = null;
                }
                else
                {
                    _mesher.enableDebugLog = false;
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
                break;
        }
    }
}


