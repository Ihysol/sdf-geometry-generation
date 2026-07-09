using System;
using NUnit.Framework;
using UnityEngine;

public class PipelineTests
{
    private VolumeLayout _layout;

    [SetUp]
    public void Setup()
    {
        // 32x32x32 grid, unit cube
        _layout = new VolumeLayout
        {
            Resolution = new Vector3Int(32, 32, 32),
            CellSize = 1f / 32f,
            Origin = new Vector3(-0.5f, -0.5f, -0.5f),
            ChunkSize = 8,
            IsoLevel = 0f
        };
    }

    [Test]
    public void Buffer_CanBeCreatedAndDisposed()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        Assert.That(buffer.DensityCpu.IsCreated, Is.True);
        Assert.That(buffer.MaterialCpu.IsCreated, Is.True);
        Assert.That(buffer.TotalChunks, Is.GreaterThan(0));

        buffer.Dispose();
    }

    [Test]
    public void Buffer_InitializeSetsValues()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(0.5f, 42);

        for (int i = 0; i < _layout.TotalCells; i++)
        {
            Assert.That(buffer.DensityCpu[i], Is.EqualTo(0.5f));
            Assert.That(buffer.MaterialCpu[i], Is.EqualTo(42));
        }

        buffer.Dispose();
    }

    [Test]
    public void Buffer_DirtyChunksWork()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize();
        var dirtySystem = new DirtyChunkSystem();
        dirtySystem.Initialize(buffer);

        // Mark a region dirty
        var region = new BoundsInt(0, 0, 0, 8, 8, 8);
        dirtySystem.MarkDirty(region);
        Assert.That(dirtySystem.HasPendingWork, Is.True);

        dirtySystem.ClearAllDirty();
        Assert.That(dirtySystem.HasPendingWork, Is.False);

        buffer.Dispose();
    }

    [Test]
    public void AddSphereOperation_ModifiesDensity()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(1f);

        var op = new AddSphereOperation(Vector3.zero, 0.25f, 1);
        op.ComputeAffectedRegion(_layout);
        op.ApplyCpu(buffer);

        // Center should be inside sphere (negative density)
        Vector3Int centerIdx = _layout.WorldToIndex(Vector3.zero);
        int offset = _layout.IndexToOffset(centerIdx);
        Assert.That(buffer.DensityCpu[offset], Is.LessThan(0f));
        Assert.That(buffer.MaterialCpu[offset], Is.EqualTo(1));

        buffer.Dispose();
    }

    [Test]
    public void SubtractSphereOperation_ModifiesDensity()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(-1f); // Start filled

        var op = new SubtractSphereOperation(Vector3.zero, 0.25f);
        op.ComputeAffectedRegion(_layout);
        op.ApplyCpu(buffer);

        // Center should be outside (positive density after subtraction)
        Vector3Int centerIdx = _layout.WorldToIndex(Vector3.zero);
        int offset = _layout.IndexToOffset(centerIdx);
        Assert.That(buffer.DensityCpu[offset], Is.GreaterThan(0f));

        buffer.Dispose();
    }

    [Test]
    public void PaintMaterialOperation_ChangesMaterial()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(0f, 0);

        var op = new PaintMaterialOperation(Vector3.zero, 0.25f, 7);
        op.ComputeAffectedRegion(_layout);
        op.ApplyCpu(buffer);

        // Center should have material ID 7
        Vector3Int centerIdx = _layout.WorldToIndex(Vector3.zero);
        int offset = _layout.IndexToOffset(centerIdx);
        Assert.That(buffer.MaterialCpu[offset], Is.EqualTo(7));

        buffer.Dispose();
    }

    [Test]
    public void SmoothOperation_AveragesDensity()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(0f);

        // Set a single cell to 1.0 at center
        Vector3Int centerIdx = _layout.WorldToIndex(Vector3.zero);
        int offset = _layout.IndexToOffset(centerIdx);
        var d = buffer.DensityCpu;
        d[offset] = 1f;

        var op = new SmoothOperation(Vector3.zero, 0.5f, 1);
        op.ComputeAffectedRegion(_layout);
        op.ApplyCpu(buffer);

        // Center should be smoothed (less than 1.0)
        Assert.That(buffer.DensityCpu[offset], Is.LessThan(1f));

        buffer.Dispose();
    }

    [Test]
    public void CopyPasteOperation_FullCycle()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(0f, 0);

        // Paint a sphere to copy
        var addOp = new AddSphereOperation(new Vector3(0.2f, 0f, 0f), 0.15f, 1);
        addOp.ComputeAffectedRegion(_layout);
        addOp.ApplyCpu(buffer);

        // Copy region
        var copyRegion = new BoundsInt(8, 8, 8, 8, 8, 8);
        var copyOp = new CopyOperation(copyRegion);
        copyOp.ApplyCpu(buffer);

        Assert.That(VolumeClipboard.Instance.HasData, Is.True);

        // Paste at different position
        var pasteOp = new PasteOperation(new Vector3Int(16, 16, 16), VolumeClipboard.Instance);
        pasteOp.ApplyCpu(buffer);

        // Check paste region has copied data
        bool hasNonZeroDensity = false;
        for (int z = 16; z < 24 && z < _layout.Resolution.z; z++)
        {
            for (int y = 16; y < 24 && y < _layout.Resolution.y; y++)
            {
                for (int x = 16; x < 24 && x < _layout.Resolution.x; x++)
                {
                    int off = _layout.IndexToOffset(new Vector3Int(x, y, z));
                    if (buffer.DensityCpu[off] != 0f)
                        hasNonZeroDensity = true;
                }
            }
        }

        Assert.That(hasNonZeroDensity, Is.True);

        VolumeClipboard.Instance.Clear();
        buffer.Dispose();
    }

    [Test]
    public void FeatureDefinition_CanEvaluate()
    {
        // Create a temporary ScriptableObject for testing
        var def = ScriptableObject.CreateInstance<FeatureDefinition>();
        def.displayName = "TestSphere";
        def.shapeType = VolumeShapeType.Sphere;
        def.sphereRadius = 1f;

        float d = def.EvaluateLocal(new Vector3(0.5f, 0f, 0f));
        Assert.That(d, Is.EqualTo(-0.5f));

        float d2 = def.EvaluateLocal(new Vector3(2f, 0f, 0f));
        Assert.That(d2, Is.EqualTo(1f));

        ScriptableObject.DestroyImmediate(def);
    }

    [Test]
    public void FeatureInstance_TransformsCorrectly()
    {
        var def = ScriptableObject.CreateInstance<FeatureDefinition>();
        def.shapeType = VolumeShapeType.Sphere;
        def.sphereRadius = 1f;
        def.defaultMaterialId = 5;

        var instance = new FeatureInstance(def, new Vector3(3f, 0f, 0f));

        // Point at (4,0,0) is 1 unit from center → on surface
        float d = instance.Evaluate(new Vector3(4f, 0f, 0f));
        Assert.That(d, Is.EqualTo(0f).Within(1e-5f));

        var bounds = instance.GetBounds();
        Assert.That(bounds.center, Is.EqualTo(new Vector3(3f, 0f, 0f)));

        ScriptableObject.DestroyImmediate(def);
    }

    [Test]
    public void FeatureLibrary_ManagesFeatures()
    {
        var lib = ScriptableObject.CreateInstance<FeatureLibrary>();

        var def1 = ScriptableObject.CreateInstance<FeatureDefinition>();
        def1.displayName = "Sphere";
        def1.shapeType = VolumeShapeType.Sphere;

        var def2 = ScriptableObject.CreateInstance<FeatureDefinition>();
        def2.displayName = "Box";
        def2.shapeType = VolumeShapeType.Box;

        lib.AddFeature(def1);
        lib.AddFeature(def2);

        Assert.That(lib.GetFeature("Sphere"), Is.EqualTo(def1));
        Assert.That(lib.GetFeature("Box"), Is.EqualTo(def2));
        Assert.That(lib.GetFeaturesByShape(VolumeShapeType.Sphere).Count, Is.EqualTo(1));

        var instance = lib.CreateInstance("Box", new Vector3(1f, 0f, 0f));
        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.Position, Is.EqualTo(new Vector3(1f, 0f, 0f)));

        ScriptableObject.DestroyImmediate(lib);
        ScriptableObject.DestroyImmediate(def1);
        ScriptableObject.DestroyImmediate(def2);
    }

    [Test]
    public void MesherFactory_ReturnsGpuVoxel()
    {
        var mesher = MesherFactory.Create(PipelineMesherType.GpuVoxel);
        Assert.That(mesher, Is.InstanceOf<GpuVoxelMesher>());
        Assert.That(mesher.SupportsGpu, Is.True);
        Assert.That(mesher.SupportsCpu, Is.False);
    }

    [Test]
    public void VoxelMesher_BuildsMesh()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);

        // Carve a sphere into the volume
        var addOp = new AddSphereOperation(Vector3.zero, 0.4f, 0);
        addOp.ComputeAffectedRegion(_layout);
        addOp.ApplyCpu(buffer);

        var mesher = new VoxelMesher();
        MeshingContext ctx = MeshingContext.Default(_layout);
        var meshData = mesher.BuildCpu(buffer, ctx);

        Assert.That(meshData.VertexCount, Is.GreaterThan(0));
        Assert.That(meshData.IndexCount, Is.GreaterThan(0));

        meshData.Dispose();
        buffer.Dispose();
    }

    [Test]
    public void Pipeline_CanRebuild()
    {
        var mesher = new VoxelMesher();
        var pipeline = new VolumePipeline(_layout, mesher);

        var output = new UnityMeshOutput(new Mesh());
        pipeline.Initialize(output);

        // Create a simple mock source
        var addOp = new AddSphereOperation(Vector3.zero, 0.3f, 1);
        addOp.ComputeAffectedRegion(_layout);
        ((ChunkedFlatVolumeBuffer)pipeline.Buffer).Initialize(1f);
        addOp.ApplyCpu(pipeline.Buffer);

        pipeline.ApplyOperation(addOp);
        Assert.That(pipeline.IsDirty, Is.True);

        pipeline.Dispose();
    }
}
