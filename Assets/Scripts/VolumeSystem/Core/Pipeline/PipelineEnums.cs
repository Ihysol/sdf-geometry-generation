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
    DualContouring
}

public enum OutputMode
{
    UnityMesh,
    ProceduralDraw,
    RaymarchVolume,
    Debug
}
