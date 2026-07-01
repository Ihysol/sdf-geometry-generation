using NUnit.Framework;
using UnityEngine;

public class FlatOctreeBurstFrontierTests
{
    [Test]
    public void Build_WithBuiltInComposer_UsesFrontierJobBatches()
    {
        GameObject root = new GameObject("frontier-root");
        GameObject sphereObject = new GameObject("sphere");
        try
        {
            sphereObject.transform.SetParent(root.transform, false);
            sphereObject.transform.localPosition = Vector3.zero;
            VolumeObject sphere = sphereObject.AddComponent<VolumeObject>();
            sphere.shapeType = VolumeShapeType.Sphere;
            sphere.role = VolumeOperationRole.Add;
            sphere.sphereRadius = 1.25f;

            VolumeSceneComposer composer = root.AddComponent<VolumeSceneComposer>();
            composer.objects.Add(sphere);
            composer.RebuildComposition();

            FlatOctreeVolumeBuilder builder = new FlatOctreeVolumeBuilder
            {
                center = Vector3.zero,
                size = Vector3.one * 4f,
                boundsPadding = 0f,
                minDepth = 2,
                maxDepth = 5,
                edgeRefinementSteps = 1,
                useBurstFrontier = true,
                useBurstPreFill = false,
                suppressBuildLog = true
            };

            OctreeVolume volume = builder.Build(composer);

            Assert.That(volume, Is.Not.Null);
            Assert.That(builder.LastBuildStats.frontierUsed, Is.True);
            Assert.That(builder.LastBuildStats.frontierBatchCount, Is.GreaterThan(0));
            Assert.That(builder.LastBuildStats.frontierSampleCount, Is.GreaterThanOrEqualTo(32));
            Assert.That(builder.LastBuildStats.frontierJobSampleCount, Is.GreaterThan(0));
            Assert.That(
                builder.LastBuildStats.frontierJobSampleCount + builder.LastBuildStats.frontierSerialSampleCount,
                Is.EqualTo(builder.LastBuildStats.frontierSampleCount));
            Assert.That(builder.LastBuildStats.frontierBuildReplayMs, Is.EqualTo(0d));
            Assert.That(builder.LastBuildStats.frontierNodeRecordMs, Is.GreaterThan(0d));
            Assert.That(builder.LastBuildStats.frontierTraversalMs, Is.GreaterThanOrEqualTo(0d));
            Assert.That(builder.LastBuildStats.frontierCollectCornersMs, Is.GreaterThanOrEqualTo(0d));
            Assert.That(builder.LastBuildStats.frontierCollectCentersMs, Is.GreaterThanOrEqualTo(0d));
            Assert.That(builder.LastBuildStats.frontierSubdivideDecisionMs, Is.GreaterThanOrEqualTo(0d));
            Assert.That(builder.LastBuildStats.frontierEnqueueChildrenMs, Is.GreaterThanOrEqualTo(0d));
        }
        finally
        {
            Object.DestroyImmediate(sphereObject);
            Object.DestroyImmediate(root);
        }
    }
}
