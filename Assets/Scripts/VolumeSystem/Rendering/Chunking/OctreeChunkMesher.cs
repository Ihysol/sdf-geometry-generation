#if LEGACY
using UnityEngine;

public class OctreeChunkMesher : IChunkMesher<OctreeVolume>
{
    public readonly struct FlatDualContouringChunkSettings
    {
        public readonly float IsoLevel;
        public readonly bool UseQefVertices;
        public readonly QefVertexMode QefVertexMode;
        public readonly float QefBlendFactor;
        public readonly float QefSnapEpsilon;
        public readonly float QefMaxOffsetCells;
        public readonly float QefAxisSnapStrength;
        public readonly bool QefEnableMultiHermite;
        public readonly int QefHermiteSamplesPerEdge;

        public FlatDualContouringChunkSettings(VolumeProcessor model)
        {
            IsoLevel = model.isoLevel;
            UseQefVertices = model.GetEffectiveUseQefVertices();
            QefVertexMode = model.GetEffectiveQefVertexMode();
            QefBlendFactor = model.qefBlendFactor;
            QefSnapEpsilon = model.qefSnapEpsilon;
            QefMaxOffsetCells = model.qefMaxOffsetCells;
            QefAxisSnapStrength = model.qefAxisSnapStrength;
            QefEnableMultiHermite = model.GetEffectiveQefEnableMultiHermite();
            QefHermiteSamplesPerEdge = model.qefHermiteSamplesPerEdge;
        }
    }

    private readonly DualContouringOctreeMesher _mesher = new();
    private readonly DualContouringFlatOctreeMesher _flatMesher = new();
    private readonly DualMarchingCubesOctreeMesher _dualMarchingCubesMesher = new();
    private readonly DualMarchingTetrahedraOctreeMesher _dualMarchingTetrahedraMesher = new();
    private readonly SurfaceNetsOctreeMesher _surfaceNetsMesher = new();

    public void BuildChunk(
        VolumeProcessor model,
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
                if (model.GetEffectiveStorageMode() == VolumeStorageMode.Flat)
                {
                    IFlatAdaptiveVolumeData flatVolume = model.GetActiveFlatAdaptiveVolume() ?? volume;
                    _flatMesher.enableDebugLog = false;
                    _flatMesher.isoLevel = model.isoLevel;
                    _flatMesher.useQefVertices = model.GetEffectiveUseQefVertices();
                    _flatMesher.qefVertexMode = model.GetEffectiveQefVertexMode();
                    _flatMesher.qefBlendFactor = model.qefBlendFactor;
                    _flatMesher.qefSnapEpsilon = model.qefSnapEpsilon;
                    _flatMesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
                    _flatMesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
                    _flatMesher.qefEnableMultiHermite = model.GetEffectiveQefEnableMultiHermite();
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
                    _mesher.useQefVertices = model.GetEffectiveUseQefVertices();
                    _mesher.qefVertexMode = model.GetEffectiveQefVertexMode();
                    _mesher.qefBlendFactor = model.qefBlendFactor;
                    _mesher.qefSnapEpsilon = model.qefSnapEpsilon;
                    _mesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
                    _mesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
                    _mesher.qefEnableMultiHermite = model.GetEffectiveQefEnableMultiHermite();
                    _mesher.qefHermiteSamplesPerEdge = model.qefHermiteSamplesPerEdge;
                    _mesher.edgeRefinementSteps = model.GetEffectiveEdgeRefinementSteps();
                    _mesher.ownedBounds = coreBounds;
                    _mesher.BuildMesh(volume, model.isoLevel, targetMesh);
                    _mesher.ownedBounds = null;
                }
                break;
        }
    }

    public bool TryBuildChunkData(
        VolumeProcessor model,
        OctreeVolume volume,
        Bounds coreBounds,
        out MeshData meshData)
    {
        meshData = null;

        if (model == null || volume == null)
            return false;

        if (model.octreeMesherType != OctreeMesherType.DualContouring ||
            model.GetEffectiveStorageMode() != VolumeStorageMode.Flat)
            return false;

        IFlatAdaptiveVolumeData flatVolume = model.GetActiveFlatAdaptiveVolume() ?? volume;
        FlatDualContouringChunkSettings settings = new(model);
        meshData = BuildFlatDualContouringChunkData(settings, flatVolume, coreBounds);
        return true;
    }

    public static MeshData BuildFlatDualContouringChunkData(
        FlatDualContouringChunkSettings settings,
        IFlatAdaptiveVolumeData flatVolume,
        Bounds coreBounds)
    {
        DualContouringFlatOctreeMesher flatMesher = new()
        {
            enableDebugLog = false,
            isoLevel = settings.IsoLevel,
            useQefVertices = settings.UseQefVertices,
            qefVertexMode = settings.QefVertexMode,
            qefBlendFactor = settings.QefBlendFactor,
            qefSnapEpsilon = settings.QefSnapEpsilon,
            qefMaxOffsetCells = settings.QefMaxOffsetCells,
            qefAxisSnapStrength = settings.QefAxisSnapStrength,
            qefEnableMultiHermite = settings.QefEnableMultiHermite,
            qefHermiteSamplesPerEdge = settings.QefHermiteSamplesPerEdge,
            ownedBounds = coreBounds,
            ownedBoundsList = null
        };

        return flatMesher.BuildMeshData(flatVolume, settings.IsoLevel);
    }
}


#endif
