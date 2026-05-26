using UnityEngine;

public interface IFlatAdaptiveVolumeData : IVolumeData
{
    int MaxDepth { get; }
    Vector3 GridOrigin { get; }
    Vector3 CellSize { get; }
    IScalarFieldSource Source { get; }
    FlatOctreeLayout GetFlatLayout(bool includeCornerValues = false);
}
