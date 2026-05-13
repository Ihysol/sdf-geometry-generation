using System.Collections.Generic;
using UnityEngine;

public class ChunkSeamStitcher
    : IChunkSeamStitcher
{
    private readonly DualContouringOctreeMesher _mesher = new();
    private readonly List<Bounds> _seamBounds = new();

    public bool CanHandle(VolumeModel model, IVolumeData activeVolume)
    {
        return model != null && model.dataStructure == VolumeDataStructure.Octree;
    }

    /// <summary>Builds optional seam-only geometry around internal chunk borders.</summary>
    public void RebuildSeams(
        VolumeModel model,
        IScalarFieldSource source,
        Bounds globalBounds,
        Vector3Int chunkCount,
        Mesh seamMesh)
    {
        seamMesh.Clear();
        _seamBounds.Clear();

        OctreeVolumeBuilder template = model.octreeSampler.builder;

        int resolution = 1 << template.maxDepth;
        Vector3 globalCellSize = globalBounds.size / resolution;

        BuildSeamBounds(
            globalBounds,
            chunkCount,
            globalCellSize,
            _seamBounds
        );

        if (_seamBounds.Count == 0)
            return;

        OctreeVolumeBuilder builder = new OctreeVolumeBuilder
        {
            center = globalBounds.center,
            size = globalBounds.size,
            boundsPadding = 0f,

            minDepth = template.minDepth,
            maxDepth = template.maxDepth,

            useGlobalGrid = true,
            globalOrigin = globalBounds.min,
            globalCellSize = globalCellSize
        };

        OctreeVolume volume = builder.Build(source);

        _mesher.isoLevel = model.isoLevel;
        _mesher.ownedBounds = null;
        _mesher.ownedBoundsList = _seamBounds;
        _mesher.BuildMesh(volume, seamMesh);
        _mesher.ownedBoundsList = null;

        if (model.recalculateNormals)
            seamMesh.RecalculateNormals();

        if (model.recalculateBounds)
            seamMesh.RecalculateBounds();
    }

    /// <summary>Creates thin ownership bounds along every internal chunk split plane.</summary>
    private void BuildSeamBounds(
        Bounds globalBounds,
        Vector3Int chunkCount,
        Vector3 cellSize,
        List<Bounds> result)
    {
        Vector3 chunkSize = new Vector3(
            globalBounds.size.x / chunkCount.x,
            globalBounds.size.y / chunkCount.y,
            globalBounds.size.z / chunkCount.z
        );

        Vector3 thickness = cellSize * 3f;

        // X seams
        for (int x = 1; x < chunkCount.x; x++)
        {
            float px = globalBounds.min.x + chunkSize.x * x;

            Bounds b = new Bounds(
                new Vector3(px, globalBounds.center.y, globalBounds.center.z),
                new Vector3(thickness.x, globalBounds.size.y, globalBounds.size.z)
            );

            result.Add(b);
        }

        // Y seams
        for (int y = 1; y < chunkCount.y; y++)
        {
            float py = globalBounds.min.y + chunkSize.y * y;

            Bounds b = new Bounds(
                new Vector3(globalBounds.center.x, py, globalBounds.center.z),
                new Vector3(globalBounds.size.x, thickness.y, globalBounds.size.z)
            );

            result.Add(b);
        }

        // Z seams
        for (int z = 1; z < chunkCount.z; z++)
        {
            float pz = globalBounds.min.z + chunkSize.z * z;

            Bounds b = new Bounds(
                new Vector3(globalBounds.center.x, globalBounds.center.y, pz),
                new Vector3(globalBounds.size.x, globalBounds.size.y, thickness.z)
            );

            result.Add(b);
        }
    }
}
