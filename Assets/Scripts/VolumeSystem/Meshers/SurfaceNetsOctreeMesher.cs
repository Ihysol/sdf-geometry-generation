using System.Collections.Generic;
using UnityEngine;

public class SurfaceNetsOctreeMesher : IVolumeMesher<OctreeVolume>
{
    private readonly DualContouringOctreeMesher _backend = new();

    public Bounds? ownedBounds
    {
        get => _backend.ownedBounds;
        set => _backend.ownedBounds = value;
    }

    public List<Bounds> ownedBoundsList
    {
        get => _backend.ownedBoundsList;
        set => _backend.ownedBoundsList = value;
    }

    public void BuildMesh(OctreeVolume volume, float isoLevel, Mesh targetMesh)
    {
        _backend.enableDebugLog = false;
        _backend.isoLevel = isoLevel;
        _backend.useQefVertices = false;
        _backend.qefVertexMode = QefVertexMode.AverageCrossings;
        _backend.BuildMesh(volume, isoLevel, targetMesh);
    }
}
