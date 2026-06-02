using NUnit.Framework;
using UnityEngine;

public class OctreeVolumeBuilderProfilingTests
{
    [Test]
    public void Build_RecordsSamplingAndCacheStatistics()
    {
        OctreeVolumeBuilder builder = new OctreeVolumeBuilder
        {
            center = Vector3.zero,
            size = Vector3.one * 2f,
            boundsPadding = 0f,
            minDepth = 1,
            maxDepth = 2,
            useQefVertices = false,
            suppressBuildLog = true
        };
        CountingSphereSource source = new CountingSphereSource();

        OctreeVolume volume = builder.Build(source);
        OctreeVolumeBuilder.BuildStats stats = builder.LastBuildStats;

        Assert.That(builder.edgeRefinementSteps, Is.EqualTo(3));
        Assert.That(volume, Is.Not.Null);
        Assert.That(stats.totalNodes, Is.EqualTo(volume.TotalNodes));
        Assert.That(stats.surfaceLeaves, Is.EqualTo(volume.SurfaceLeaves));
        Assert.That(stats.sourceEvaluations, Is.EqualTo(source.Evaluations));
        Assert.That(stats.cornerCacheHits, Is.GreaterThan(0));
        Assert.That(stats.cornerCacheMisses, Is.GreaterThan(0));
        Assert.That(stats.centerCacheMisses, Is.GreaterThan(0));
        Assert.That(stats.centerDirectEvaluations, Is.GreaterThan(0));
        Assert.That(stats.gradientCacheMisses, Is.EqualTo(stats.gradientEvaluations));
        Assert.That(stats.hermiteCacheMisses, Is.GreaterThan(0));
        Assert.That(stats.subdivisionMinDepth, Is.GreaterThan(0));
        Assert.That(stats.subdivisionCornerCrossing, Is.GreaterThan(0));
        Assert.That(stats.subdivisionDistanceThreshold, Is.GreaterThan(0));
        Assert.That(volume.CachedHermiteSampleCount, Is.EqualTo(stats.hermiteCacheMisses));
        Assert.That(volume.Root.CornerValues, Is.Null);
        AssertSurfaceLeavesRetainCorners(volume.Root);
        Assert.That(stats.totalMs, Is.GreaterThanOrEqualTo(stats.recursiveBuildMs));
        Assert.That(stats.recursiveBuildMs, Is.GreaterThanOrEqualTo(stats.surfaceVertexMs));
    }

    [Test]
    public void Build_RespectsConfiguredEdgeRefinementSteps()
    {
        OctreeVolumeBuilder builder = new OctreeVolumeBuilder
        {
            center = Vector3.zero,
            size = Vector3.one * 2f,
            boundsPadding = 0f,
            minDepth = 1,
            maxDepth = 2,
            edgeRefinementSteps = 0,
            suppressBuildLog = true
        };

        builder.Build(new CountingSphereSource());

        Assert.That(builder.LastBuildStats.hermiteCacheMisses, Is.GreaterThan(0));
        Assert.That(builder.LastBuildStats.edgeRefinementEvaluations, Is.Zero);
    }

    private sealed class CountingSphereSource : IScalarFieldSource
    {
        public int Evaluations { get; private set; }

        public float Evaluate(Vector3 worldPosition)
        {
            Evaluations++;
            return worldPosition.magnitude - 0.75f;
        }
    }

    private static void AssertSurfaceLeavesRetainCorners(OctreeNode node)
    {
        if (node.IsLeaf)
        {
            if (node.ContainsSurface)
                Assert.That(node.CornerValues, Has.Length.EqualTo(8));
            return;
        }

        foreach (OctreeNode child in node.Children)
            AssertSurfaceLeavesRetainCorners(child);
    }
}
