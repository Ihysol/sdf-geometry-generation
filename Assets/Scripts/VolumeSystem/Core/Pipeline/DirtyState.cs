public enum DirtyState
{
    Clean,
    DirtyData,
    DirtyMesh,
    MeshingQueued,
    MeshReady,
    Uploaded
}

public enum DirtyReason
{
    Operation,
    FullRebuild,
    MesherSwitch,
    NeighborExpansion
}
