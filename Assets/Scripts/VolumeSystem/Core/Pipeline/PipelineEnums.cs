using UnityEngine;

public enum PipelineDataStructureType
{
    Sdf,
    VoxelGrid,
    SparseVoxelOctree
}

public enum PipelineStorageMode
{
    Tree,
    Flat
}

public enum ComputeBackend
{
    CPU,
    GPU
}

public enum PipelineMesherType
{
    Voxel,
    GreedyVoxel,
    MarchingCubes,
    SurfaceNets,
    DualContouring,
    GpuVoxel
}

public enum OutputMode
{
    UnityMesh,
    ProceduralDraw,
    RaymarchVolume,
    Debug
}

/// <summary>Data structure for volume representation (legacy refs from v10 plan).</summary>
public enum VolumeDataStructure
{
    VoxelGrid,
    Octree,
    SparseVoxelOctree
}

/// <summary>Dual contouring QEF vertex position strategy.</summary>
public enum QefVertexMode
{
    AverageCrossings,
    QefFeaturePreserving,
    QefAxisSnap
}

/// <summary>QEF feature class weighting for adaptive blending.</summary>
public enum QefFeatureClassWeightMode
{
    Off,
    Uniform,
    Adaptive
}
