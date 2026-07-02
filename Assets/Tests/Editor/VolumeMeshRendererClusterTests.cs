using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class VolumeMeshRendererClusterTests
{
    [Test]
    public void BuildNeighborChunkClustersForTests_GroupsTouchingChunksAndSeparatesGaps()
    {
        var bounds = new List<Bounds>
        {
            new(new Vector3(0.5f, 0.5f, 0.5f), Vector3.one),
            new(new Vector3(1.5f, 0.5f, 0.5f), Vector3.one),
            new(new Vector3(2.5f, 0.5f, 0.5f), Vector3.one),
            new(new Vector3(8.5f, 0.5f, 0.5f), Vector3.one),
        };

        List<List<int>> clusters = VolumeMeshRenderer.BuildNeighborChunkClustersForTests(bounds);

        Assert.That(clusters, Has.Count.EqualTo(2));
        Assert.That(clusters[0], Is.EquivalentTo(new[] { 0, 1, 2 }));
        Assert.That(clusters[1], Is.EquivalentTo(new[] { 3 }));
    }

    [Test]
    public void RenderStats_ExposesClusterDiagnostics()
    {
        Assert.That(typeof(VolumeMeshRenderer.RenderStats).GetField("chunkClusters"), Is.Not.Null);
        Assert.That(typeof(VolumeMeshRenderer.RenderStats).GetField("chunkClusterBuildMs"), Is.Not.Null);
        Assert.That(typeof(VolumeMeshRenderer.RenderStats).GetField("chunkClusterBuilds"), Is.Not.Null);
        Assert.That(typeof(VolumeMeshRenderer.RenderStats).GetField("chunkMeshBuildMs"), Is.Not.Null);
        Assert.That(typeof(VolumeMeshRenderer.RenderStats).GetField("chunkApplyMeshMs"), Is.Not.Null);
        Assert.That(typeof(VolumeMeshRenderer.RenderStats).GetField("chunkClusterFallbacks"), Is.Not.Null);
    }

    [Test]
    public void RebuildProfileSample_ExposesChunkRendererBreakdown()
    {
        Assert.That(typeof(VolumeModel.RebuildProfileSample).GetField("rendererChunkClusterBuildMs"), Is.Not.Null);
        Assert.That(typeof(VolumeModel.RebuildProfileSample).GetField("rendererChunkMeshBuildMs"), Is.Not.Null);
        Assert.That(typeof(VolumeModel.RebuildProfileSample).GetField("rendererChunkApplyMeshMs"), Is.Not.Null);
    }
}
