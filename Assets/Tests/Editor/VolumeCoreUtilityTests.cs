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
    public void FlatOctreeCrossingInvalidation_UsesCachedCrossingPointForPreciseInvalidation()
    {
        Bounds dirtyBounds = new Bounds(new Vector3(5.2f, 5f, 5f), new Vector3(0.2f, 0.2f, 0.2f));

        Assert.That(FlatOctreeVolumeBuilder.CrossingPointOverlapsBounds(new Vector3(4.8f, 5f, 5f), dirtyBounds), Is.False);
        Assert.That(FlatOctreeVolumeBuilder.CrossingPointOverlapsBounds(new Vector3(5.2f, 5f, 5f), dirtyBounds), Is.True);
    }

    [Test]
    public void FlatOctreePackedEdgeKey_IsOrderIndependentAndUniquePerGridEdge()
    {
        const int gridVertexSide = 65;
        Vector3Int start = new Vector3Int(4, 5, 6);

        ulong xEdge = FlatOctreeVolumeBuilder.PackGridEdgeKey(
            start,
            start + Vector3Int.right,
            gridVertexSide);
        ulong reversedXEdge = FlatOctreeVolumeBuilder.PackGridEdgeKey(
            start + Vector3Int.right,
            start,
            gridVertexSide);
        ulong yEdge = FlatOctreeVolumeBuilder.PackGridEdgeKey(
            start,
            start + Vector3Int.up,
            gridVertexSide);
        ulong adjacentXEdge = FlatOctreeVolumeBuilder.PackGridEdgeKey(
            start + Vector3Int.up,
            start + Vector3Int.up + Vector3Int.right,
            gridVertexSide);

        Assert.That(reversedXEdge, Is.EqualTo(xEdge));
        Assert.That(yEdge, Is.Not.EqualTo(xEdge));
        Assert.That(adjacentXEdge, Is.Not.EqualTo(xEdge));
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
    public void FlatOctreeLayout_DenseLeafLookupResolvesCellsInsideLargeLeaf()
    {
        FlatOctreeLayout layout = new FlatOctreeLayout
        {
            Centers = new[] { Vector3.zero },
            Sizes = new[] { Vector3.one * 4f },
            SurfaceVertices = new[] { Vector3.zero },
            SurfaceNormals = new[] { Vector3.up },
            Coords = new[] { Vector3Int.zero },
            NodeSizeInCells = new[] { new Vector3Int(4, 4, 4) },
            CornerValues8 = new float[8],
            FirstChildIndex = new[] { -1 },
            ChildMask = new byte[1],
            Flags = new[] { FlatOctreeLayout.FlagLeaf }
        };
        layout.SetCount(1);

        layout.EnsureRuntimeCache();

        Assert.That(layout.TryGetContainingLeafIndex(new Vector3Int(3, 2, 1), out int nodeIndex), Is.True);
        Assert.That(nodeIndex, Is.EqualTo(0));
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
    public void FlatOctreeDirtyBuild_ReusesCleanSubtreesWithoutChangingLayout()
    {
        FlatOctreeVolumeBuilder incrementalBuilder = CreateFlatBuilderForDirtyReuseTest();
        MovingSphereSource incrementalSource = new MovingSphereSource(
            new Vector3(-0.35f, 0f, 0f),
            0.55f);
        incrementalBuilder.Build(incrementalSource);

        incrementalSource.Center = new Vector3(0.35f, 0f, 0f);
        incrementalSource.ResetCount();
        incrementalBuilder.PreparePersistentCrossingCache(
            hasDirtyBounds: true,
            dirtyBounds: new Bounds(Vector3.zero, new Vector3(2f, 1.2f, 1.2f)));
        FlatOctreeLayout incrementalLayout = incrementalBuilder
            .Build(incrementalSource)
            .GetFlatLayout(includeCornerValues: true);
        int incrementalEvaluations = incrementalSource.EvaluateCount;

        FlatOctreeVolumeBuilder fullBuilder = CreateFlatBuilderForDirtyReuseTest();
        MovingSphereSource fullSource = new MovingSphereSource(incrementalSource.Center, 0.55f);
        FlatOctreeLayout fullLayout = fullBuilder
            .Build(fullSource)
            .GetFlatLayout(includeCornerValues: true);

        Assert.That(incrementalBuilder.LastBuildStats.reusedNodeCount, Is.GreaterThan(0));
        Assert.That(incrementalEvaluations, Is.LessThan(fullSource.EvaluateCount));
        AssertFlatLayoutsEquivalent(incrementalLayout, fullLayout);
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

    private static FlatOctreeVolumeBuilder CreateFlatBuilderForDirtyReuseTest()
    {
        return new FlatOctreeVolumeBuilder
        {
            center = Vector3.zero,
            size = Vector3.one * 4f,
            boundsPadding = 0f,
            minDepth = 2,
            maxDepth = 4,
            edgeRefinementSteps = 2,
            suppressBuildLog = true
        };
    }

    private static void AssertFlatLayoutsEquivalent(FlatOctreeLayout actual, FlatOctreeLayout expected)
    {
        Assert.That(actual.Count, Is.EqualTo(expected.Count));
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.That(actual.Centers[i], Is.EqualTo(expected.Centers[i]));
            Assert.That(actual.Sizes[i], Is.EqualTo(expected.Sizes[i]));
            Assert.That(actual.Coords[i], Is.EqualTo(expected.Coords[i]));
            Assert.That(actual.NodeSizeInCells[i], Is.EqualTo(expected.NodeSizeInCells[i]));
            Assert.That(actual.FirstChildIndex[i], Is.EqualTo(expected.FirstChildIndex[i]));
            Assert.That(actual.ChildMask[i], Is.EqualTo(expected.ChildMask[i]));
            Assert.That(actual.Flags[i], Is.EqualTo(expected.Flags[i]));
            Assert.That(actual.SurfaceVertices[i], Is.EqualTo(expected.SurfaceVertices[i]));
            Assert.That(actual.SurfaceNormals[i], Is.EqualTo(expected.SurfaceNormals[i]));

            for (int corner = 0; corner < 8; corner++)
                Assert.That(actual.GetCornerValue(i, corner), Is.EqualTo(expected.GetCornerValue(i, corner)));
        }
    }

    private sealed class MovingSphereSource : IScalarFieldSource
    {
        private readonly float _radius;

        public Vector3 Center { get; set; }
        public int EvaluateCount { get; private set; }

        public MovingSphereSource(Vector3 center, float radius)
        {
            Center = center;
            _radius = radius;
        }

        public float Evaluate(Vector3 worldPosition)
        {
            EvaluateCount++;
            return (worldPosition - Center).magnitude - _radius;
        }

        public void ResetCount()
        {
            EvaluateCount = 0;
        }
    }
}
