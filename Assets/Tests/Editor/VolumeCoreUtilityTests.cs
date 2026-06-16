using NUnit.Framework;
using UnityEngine;

public class VolumeCoreUtilityTests
{
    [Test]
    public void QuantizedVector3Key_TreatsTinyCoordinateNoiseAsSamePoint()
    {
        Vector3 origin = new Vector3(-2.25f, -2.25f, -2.25f);
        Vector3 cellSize = Vector3.one * (4.5f / 64f);
        float quantum = QuantizedVector3Key.GetQuantum(cellSize);

        QuantizedVector3Key a = QuantizedVector3Key.FromPosition(
            new Vector3(1.0000001f, 0f, 0f),
            origin,
            quantum);
        QuantizedVector3Key b = QuantizedVector3Key.FromPosition(
            new Vector3(1.0000002f, 0f, 0f),
            origin,
            quantum);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void QuantizedVector3Key_SeparatesDistinctGradientSamples()
    {
        Vector3 origin = Vector3.zero;
        Vector3 cellSize = Vector3.one * 0.1f;
        float quantum = QuantizedVector3Key.GetQuantum(cellSize);

        QuantizedVector3Key a = QuantizedVector3Key.FromPosition(Vector3.zero, origin, quantum);
        QuantizedVector3Key b = QuantizedVector3Key.FromPosition(new Vector3(0.001f, 0f, 0f), origin, quantum);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void VoxelGrid_TryGetAndTrySetRejectOutOfBoundsCoordinates()
    {
        VoxelGrid grid = new VoxelGrid(new Vector3Int(2, 2, 2), Vector3.zero, Vector3.one);

        Assert.That(grid.TrySetValue(1, 1, 1, 4.25f), Is.True);
        Assert.That(grid.TryGetValue(1, 1, 1, out float value), Is.True);
        Assert.That(value, Is.EqualTo(4.25f));

        Assert.That(grid.IsInBounds(-1, 0, 0), Is.False);
        Assert.That(grid.TrySetValue(2, 0, 0, 10f), Is.False);
        Assert.That(grid.TryGetValue(0, 2, 0, out _), Is.False);
    }

    [Test]
    public void VoxelGrid_GetAndSetThrowHelpfulExceptionForOutOfBoundsCoordinates()
    {
        VoxelGrid grid = new VoxelGrid(new Vector3Int(2, 2, 2), Vector3.zero, Vector3.one);

        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.GetValue(2, 0, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.SetValue(0, -1, 0, 1f));
    }

    [Test]
    public void OctreeHermiteEdgeKey_IsDirectionIndependent()
    {
        Vector3Int a = new Vector3Int(1, 2, 3);
        Vector3Int b = new Vector3Int(4, 5, 6);

        OctreeHermiteEdgeKey forward = new OctreeHermiteEdgeKey(a, b);
        OctreeHermiteEdgeKey reverse = new OctreeHermiteEdgeKey(b, a);

        Assert.That(forward, Is.EqualTo(reverse));
        Assert.That(forward.GetHashCode(), Is.EqualTo(reverse.GetHashCode()));
    }

    [Test]
    public void FlatOctreeCrossingInvalidation_UsesPreciseWorldBounds()
    {
        OctreeHermiteEdgeKey edge = new OctreeHermiteEdgeKey(
            new Vector3Int(4, 6, 5),
            new Vector3Int(6, 6, 5));
        Bounds dirtyBounds = new Bounds(new Vector3(5.2f, 5.2f, 5f), new Vector3(0.2f, 0.2f, 0.2f));

        Assert.That(FlatOctreeVolumeBuilder.CrossingEdgeOverlapsBounds(edge, dirtyBounds, Vector3.zero, Vector3.one), Is.False);
    }

    [Test]
    public void FlatOctreeCrossingInvalidation_InvalidatesEdgeIntersectingWorldBounds()
    {
        OctreeHermiteEdgeKey edge = new OctreeHermiteEdgeKey(
            new Vector3Int(4, 5, 5),
            new Vector3Int(6, 5, 5));
        Bounds dirtyBounds = new Bounds(new Vector3(5.2f, 5f, 5f), new Vector3(0.2f, 0.2f, 0.2f));

        Assert.That(FlatOctreeVolumeBuilder.CrossingEdgeOverlapsBounds(edge, dirtyBounds, Vector3.zero, Vector3.one), Is.True);
    }

    [Test]
    public void FlatOctreeCornerInvalidation_InvalidatesCornerInsideWorldBounds()
    {
        Bounds dirtyBounds = new Bounds(new Vector3(5.2f, 5f, 5f), new Vector3(0.5f, 0.5f, 0.5f));

        Assert.That(FlatOctreeVolumeBuilder.CornerCoordOverlapsBounds(new Vector3Int(5, 5, 5), dirtyBounds, Vector3.zero, Vector3.one), Is.True);
    }

    [Test]
    public void FlatOctreeCornerInvalidation_KeepsCornerOutsideWorldBounds()
    {
        Bounds dirtyBounds = new Bounds(new Vector3(5.2f, 5f, 5f), new Vector3(0.5f, 0.5f, 0.5f));

        Assert.That(FlatOctreeVolumeBuilder.CornerCoordOverlapsBounds(new Vector3Int(5, 6, 5), dirtyBounds, Vector3.zero, Vector3.one), Is.False);
    }

    [Test]
    public void FlatOctreeSampleCachePadding_DefaultDoesNotExpandDirtyBounds()
    {
        Bounds dirtyBounds = new Bounds(Vector3.zero, Vector3.one);

        Bounds expanded = FlatOctreeVolumeBuilder.ExpandBoundsByCellPadding(dirtyBounds, Vector3.one, 0f);

        Assert.That(expanded.size, Is.EqualTo(dirtyBounds.size));
    }

    [Test]
    public void FlatOctreeSampleCachePadding_ExpandsByCellSizeWhenConfigured()
    {
        Bounds dirtyBounds = new Bounds(Vector3.zero, Vector3.one);

        Bounds expanded = FlatOctreeVolumeBuilder.ExpandBoundsByCellPadding(dirtyBounds, Vector3.one * 0.5f, 2f);

        Assert.That(expanded.size, Is.EqualTo(Vector3.one * 3f));
    }

    [Test]
    public void FlatOctreePersistentCornerCache_ReusesCornersOutsideDirtyBounds()
    {
        FlatOctreeVolumeBuilder builder = new FlatOctreeVolumeBuilder
        {
            center = Vector3.zero,
            size = Vector3.one * 2f,
            boundsPadding = 0f,
            minDepth = 1,
            maxDepth = 3,
            edgeRefinementSteps = 0,
            suppressBuildLog = true
        };
        CountingSphereSource source = new CountingSphereSource(0.75f);

        builder.Build(source);
        int initialMisses = builder.LastBuildStats.cornerCacheMisses;

        builder.PreparePersistentCrossingCache(
            hasDirtyBounds: true,
            dirtyBounds: new Bounds(Vector3.one * 10f, Vector3.one * 0.1f));
        builder.Build(source);

        Assert.That(initialMisses, Is.GreaterThan(0));
        Assert.That(builder.LastBuildStats.cornerCacheMisses, Is.EqualTo(0));
    }

    [Test]
    public void FlatOctreePersistentCenterCache_ReusesCentersOutsideDirtyBounds()
    {
        FlatOctreeVolumeBuilder builder = new FlatOctreeVolumeBuilder
        {
            center = Vector3.zero,
            size = Vector3.one * 2f,
            boundsPadding = 0f,
            minDepth = 1,
            maxDepth = 3,
            edgeRefinementSteps = 0,
            suppressBuildLog = true
        };
        CountingSphereSource source = new CountingSphereSource(0.75f);

        builder.Build(source);
        int initialCenterEvaluations = builder.LastBuildStats.centerEvaluations;

        source.ResetCount();
        builder.PreparePersistentCrossingCache(
            hasDirtyBounds: true,
            dirtyBounds: new Bounds(Vector3.one * 10f, Vector3.one * 0.1f));
        builder.Build(source);

        Assert.That(initialCenterEvaluations, Is.GreaterThan(0));
        Assert.That(builder.LastBuildStats.centerEvaluations, Is.GreaterThan(0));
        Assert.That(source.EvaluateCount, Is.EqualTo(0));
    }

    [Test]
    public void EdgeRefinementResidual_AcceptsSmallIsoError()
    {
        Assert.That(EdgeRefinementUtility.ResidualIsAcceptable(5e-5f), Is.True);
    }

    [Test]
    public void EdgeRefinementResidual_RejectsLargerIsoError()
    {
        Assert.That(EdgeRefinementUtility.ResidualIsAcceptable(2e-4f), Is.False);
    }

    private sealed class CountingSphereSource : IScalarFieldSource
    {
        private readonly float _radius;

        public int EvaluateCount { get; private set; }

        public CountingSphereSource(float radius)
        {
            _radius = radius;
        }

        public float Evaluate(Vector3 worldPosition)
        {
            EvaluateCount++;
            return worldPosition.magnitude - _radius;
        }

        public void ResetCount()
        {
            EvaluateCount = 0;
        }
    }
}
