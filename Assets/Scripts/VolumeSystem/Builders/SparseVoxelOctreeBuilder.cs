using UnityEngine;

[System.Serializable]
public class SparseVoxelOctreeBuilder : VolumeBuilderBase<SparseVoxelOctreeVolume>
{
    public OctreeVolumeBuilder backend = new OctreeVolumeBuilder();

    public override Bounds Bounds => backend.Bounds;

    public override SparseVoxelOctreeVolume Build(IScalarFieldSource source)
    {
        OctreeVolume octree = backend.Build(source);
        if (octree == null)
            return null;

        return new SparseVoxelOctreeVolume(
            octree.Root,
            octree.Bounds,
            octree.MaxDepth,
            octree.TotalNodes,
            octree.SurfaceLeaves,
            octree.SurfaceLeaves,
            octree.Source,
            octree.GridOrigin,
            octree.CellSize);
    }

    public bool RebuildRegion(SparseVoxelOctreeVolume existing, IScalarFieldSource source, Bounds dirtyBounds, out SparseVoxelOctreeVolume rebuilt)
    {
        rebuilt = null;
        if (existing == null)
            return false;

        OctreeVolume octreeExisting = existing.AsOctreeVolume();
        if (!backend.RebuildRegion(octreeExisting, source, dirtyBounds, out OctreeVolume octreeRebuilt) || octreeRebuilt == null)
            return false;

        rebuilt = new SparseVoxelOctreeVolume(
            octreeRebuilt.Root,
            octreeRebuilt.Bounds,
            octreeRebuilt.MaxDepth,
            octreeRebuilt.TotalNodes,
            octreeRebuilt.SurfaceLeaves,
            octreeRebuilt.SurfaceLeaves,
            octreeRebuilt.Source,
            octreeRebuilt.GridOrigin,
            octreeRebuilt.CellSize);
        return true;
    }
}
