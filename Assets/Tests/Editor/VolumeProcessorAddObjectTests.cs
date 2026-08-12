using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

public class VolumeProcessorAddObjectTests
{
    private GameObject _root;
    private VolumeProcessor _processor;
    private VolumeObjectRegistry _registry;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("VolumeProcessorAddObjectTests");
        _processor = _root.AddComponent<VolumeProcessor>();
        _registry = _root.GetComponent<VolumeObjectRegistry>();
        _processor.resolution = new Vector3Int(16, 16, 16);
        _processor.chunkSize = 8;
        _processor.boundsExtent = 4f;
        _processor.computeBackend = ComputeBackend.CPU;
    }

    [TearDown]
    public void TearDown()
    {
        if (_processor != null)
            _processor.Dispose();
        if (_root != null)
            Object.DestroyImmediate(_root);
    }

    [Test]
    public void RebuildModel_WhenBoundsDoNotFitAndAutoExpandIsOff_PreservesPreviousBuffer()
    {
        _processor.autoExpand = false;
        _processor.Initialize();

        VolumeObject first = CreateObject(Vector3.zero);
        _processor.RebuildModel();
        float[] before = CopyDensity(_processor.Pipeline.Buffer.DensityCpu);

        CreateObject(new Vector3(0f, 7f, 0f));

        LogAssert.Expect(LogType.Warning, new Regex("Objects exceed grid bounds"));
        _processor.RebuildModel();

        CollectionAssert.AreEqual(before, CopyDensity(_processor.Pipeline.Buffer.DensityCpu));
        Assert.That(_registry.objects, Has.Count.EqualTo(2));
        Assert.That(_registry.Snapshot.ShapeCount, Is.EqualTo(2));
        Assert.That(_registry.Snapshot.Evaluate(first.transform.localPosition), Is.LessThan(0f));
    }

    [Test]
    public void RebuildModel_WhenSecondObjectIsOutsideAndAutoExpandIsOn_ResizesAndBuildsBothShapes()
    {
        _processor.autoExpand = true;
        _processor.Initialize();

        VolumeObject first = CreateObject(Vector3.zero);
        VolumeObject second = CreateObject(new Vector3(0f, 7f, 0f));

        _processor.RebuildModel();

        VolumeLayout layout = _processor.Pipeline.Buffer.Layout;
        Assert.That(IsInsideLayout(layout, first.transform.position), Is.True);
        Assert.That(IsInsideLayout(layout, second.transform.position), Is.True);
        AssertBufferSampleInside(_processor.Pipeline.Buffer, first.transform.position, _processor.isoLevel);
        AssertBufferSampleInside(_processor.Pipeline.Buffer, second.transform.position, _processor.isoLevel);
    }

    [Test]
    public void RebuildDirty_AfterAutoExpand_DoesNotResizeAgainForSmallMove()
    {
        _processor.autoExpand = true;
        _processor.expandPaddingFactor = 1.25f;
        _processor.Initialize();

        VolumeObject moving = CreateObject(new Vector3(1.5f, 0f, 0f));
        _processor.RebuildModel();

        VolumePipeline expandedPipeline = _processor.Pipeline;
        VolumeLayout expandedLayout = expandedPipeline.Buffer.Layout;

        Bounds previousBounds = moving.GetEstimatedLocalBounds();
        moving.transform.localPosition += new Vector3(0.05f, 0f, 0f);
        Bounds currentBounds = moving.GetEstimatedLocalBounds();
        previousBounds.Encapsulate(currentBounds);

        _processor.MarkDirtyBounds(previousBounds);
        _processor.RebuildDirty();

        Assert.That(_processor.Pipeline, Is.SameAs(expandedPipeline),
            "A small move inside the auto-expand reserve must stay on the partial rebuild path.");
        Assert.That(_processor.Pipeline.Buffer.Layout.Origin, Is.EqualTo(expandedLayout.Origin));
        Assert.That(_processor.Pipeline.Buffer.Layout.CellSize, Is.EqualTo(expandedLayout.CellSize));
    }

    [Test]
    public void RebuildDirty_ForSmallMove_RemeshesFewerThanAllChunks()
    {
        _processor.autoExpand = false;
        _processor.resolution = new Vector3Int(64, 64, 64);
        _processor.chunkSize = 8;
        _processor.boundsExtent = 8f;
        _processor.Initialize();

        VolumeObject moving = CreateObject(Vector3.zero);
        moving.sphereRadius = 0.25f;
        _processor.RebuildModel();

        VolumePipeline pipeline = _processor.Pipeline;
        IVolumeBuffer buffer = pipeline.Buffer;
        Bounds previousBounds = moving.GetEstimatedLocalBounds();
        moving.transform.localPosition += new Vector3(0.1f, 0f, 0f);
        Bounds currentBounds = moving.GetEstimatedLocalBounds();
        previousBounds.Encapsulate(currentBounds);

        _processor.MarkDirtyBounds(previousBounds);
        _processor.RebuildDirty();

        Assert.That(_processor.Pipeline, Is.SameAs(pipeline));
        Assert.That(_processor.Pipeline.Buffer, Is.SameAs(buffer));
        Assert.That(_processor.LastRebuildWasPartial, Is.True);
        Assert.That(_processor.LastRemeshedChunkCount, Is.GreaterThan(0));
        Assert.That(_processor.LastRemeshedChunkCount, Is.LessThan(buffer.TotalChunks));
    }

    [Test]
    public void AddObject_WhenBufferAlreadyExists_UsesPartialRebuild()
    {
        _processor.autoExpand = false;
        _processor.resolution = new Vector3Int(64, 64, 64);
        _processor.chunkSize = 8;
        _processor.boundsExtent = 8f;
        _processor.Initialize();

        _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
        VolumePipeline pipeline = _processor.Pipeline;
        IVolumeBuffer buffer = pipeline.Buffer;
        int versionBeforeSecondAdd = _processor.BuildVersion;

        _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);

        Assert.That(_processor.Pipeline, Is.SameAs(pipeline));
        Assert.That(_processor.Pipeline.Buffer, Is.SameAs(buffer));
        Assert.That(_processor.BuildVersion, Is.EqualTo(versionBeforeSecondAdd + 1));
        Assert.That(_processor.LastRebuildWasPartial, Is.True);
        Assert.That(_processor.LastRemeshedChunkCount, Is.GreaterThan(0));
        Assert.That(_processor.LastRemeshedChunkCount, Is.LessThan(buffer.TotalChunks));
        Assert.That(_registry.objects, Has.Count.EqualTo(2));
    }

    [Test]
    public void AddIntersectObject_WhenBufferAlreadyExists_UsesFullRebuild()
    {
        _processor.autoExpand = false;
        _processor.resolution = new Vector3Int(32, 32, 32);
        _processor.chunkSize = 8;
        _processor.boundsExtent = 8f;
        _processor.Initialize();

        _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
        IVolumeBuffer buffer = _processor.Pipeline.Buffer;

        _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Intersect);

        Assert.That(_processor.Pipeline.Buffer, Is.SameAs(buffer));
        Assert.That(_processor.LastRebuildWasPartial, Is.False);
        Assert.That(_processor.LastRemeshedChunkCount, Is.EqualTo(buffer.TotalChunks));
    }

    [Test]
    public void AddObject_PerformsExactlyOneRebuild()
    {
        _processor.autoExpand = true;
        _processor.Initialize();
        int before = _processor.BuildVersion;

        _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);

        Assert.That(_processor.BuildVersion, Is.EqualTo(before + 1));
        Assert.That(_registry.objects, Has.Count.EqualTo(1));
    }

    [Test]
    public void NewProcessor_EnablesAutoExpandByDefault()
    {
        GameObject go = new GameObject("default-processor");
        try
        {
            VolumeProcessor processor = go.AddComponent<VolumeProcessor>();
            Assert.That(processor.autoExpand, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private VolumeObject CreateObject(Vector3 localPosition)
    {
        GameObject child = new GameObject("TestSphere");
        child.transform.SetParent(_root.transform, false);
        child.transform.localPosition = localPosition;
        VolumeObject volumeObject = child.AddComponent<VolumeObject>();
        volumeObject.shapeType = VolumeShapeType.Sphere;
        volumeObject.role = VolumeOperationRole.Add;
        volumeObject.sphereRadius = 0.75f;
        _registry.objects.Add(volumeObject);
        return volumeObject;
    }

    private static bool IsInsideLayout(VolumeLayout layout, Vector3 worldPoint)
    {
        return layout.IsInside(layout.WorldToIndex(worldPoint));
    }

    private static void AssertBufferSampleInside(IVolumeBuffer buffer, Vector3 worldPoint, float isoLevel)
    {
        VolumeLayout layout = buffer.Layout;
        Vector3Int index = layout.WorldToIndex(worldPoint);
        Assert.That(layout.IsInside(index), Is.True, $"{worldPoint} is outside {layout.Origin} / {layout.Resolution}");
        Assert.That(buffer.DensityCpu[layout.IndexToOffset(index)], Is.LessThan(isoLevel));
    }

    private static float[] CopyDensity(NativeArray<float> density)
    {
        float[] copy = new float[density.Length];
        density.CopyTo(copy);
        return copy;
    }
}
