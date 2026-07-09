using NUnit.Framework;
using UnityEngine;

public class VolumePipelineTests
{
    private VolumeLayout _layout;

    [SetUp]
    public void Setup()
    {
        _layout = new VolumeLayout
        {
            Resolution = new Vector3Int(32, 32, 32),
            CellSize = 1f / 32f,
            Origin = new Vector3(-0.5f, -0.5f, -0.5f),
            ChunkSize = 8,
            IsoLevel = 0f
        };
    }

    // --- Buffer ---

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
    public void Buffer_DirtyTracking()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize();
        var dirtySystem = new DirtyChunkSystem();
        dirtySystem.Initialize(buffer);

        Assert.That(dirtySystem.HasPendingWork, Is.False);

        dirtySystem.MarkDirty(new BoundsInt(0, 0, 0, 8, 8, 8));
        Assert.That(dirtySystem.HasPendingWork, Is.True);

        dirtySystem.ClearAllDirty();
        Assert.That(dirtySystem.HasPendingWork, Is.False);
        buffer.Dispose();
    }

    [Test]
    public void Buffer_ComputeBuffers()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize();
        buffer.EnableComputeBuffers();

        Assert.That(buffer.DensityCompute, Is.Not.Null);
        Assert.That(buffer.MaterialCompute, Is.Not.Null);
        Assert.That(buffer.HasGpuAccess, Is.True);
        buffer.Dispose();
    }

    // --- Operations ---

    [Test]
    public void AddSphere_AppliesDensity()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(1f);

        var op = new AddSphereOperation(Vector3.zero, 0.25f, 1);
        op.ComputeAffectedRegion(_layout);
        op.ApplyCpu(buffer);

        Vector3Int centerIdx = _layout.WorldToIndex(Vector3.zero);
        int offset = _layout.IndexToOffset(centerIdx);
        Assert.That(buffer.DensityCpu[offset], Is.LessThan(0f));
        Assert.That(buffer.MaterialCpu[offset], Is.EqualTo(1));
        buffer.Dispose();
    }

    [Test]
    public void SubtractSphere_AppliesDensity()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(-1f);

        var op = new SubtractSphereOperation(Vector3.zero, 0.25f);
        op.ComputeAffectedRegion(_layout);
        op.ApplyCpu(buffer);

        Vector3Int centerIdx = _layout.WorldToIndex(Vector3.zero);
        int offset = _layout.IndexToOffset(centerIdx);
        Assert.That(buffer.DensityCpu[offset], Is.GreaterThan(-1f));
        buffer.Dispose();
    }

    [Test]
    public void PaintMaterial_ChangesMaterial()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(0f, 0);

        var op = new PaintMaterialOperation(Vector3.zero, 0.25f, 7);
        op.ComputeAffectedRegion(_layout);
        op.ApplyCpu(buffer);

        Vector3Int centerIdx = _layout.WorldToIndex(Vector3.zero);
        int offset = _layout.IndexToOffset(centerIdx);
        Assert.That(buffer.MaterialCpu[offset], Is.EqualTo(7));
        buffer.Dispose();
    }

    [Test]
    public void CopyPaste_RoundTrip()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(0f, 0);

        // Paint a sphere
        var addOp = new AddSphereOperation(new Vector3(-0.2f, 0f, 0f), 0.15f, 1);
        addOp.ComputeAffectedRegion(_layout);
        addOp.ApplyCpu(buffer);

        // Copy region
        BoundsInt copyRegion = new BoundsInt(4, 8, 8, 8, 16, 16);
        var copyOp = new CopyOperation(copyRegion);
        copyOp.ApplyCpu(buffer);

        Assert.That(VolumeClipboard.Instance.HasData, Is.True);

        // Paste at different position
        var pasteOp = new PasteOperation(new Vector3Int(20, 8, 8), VolumeClipboard.Instance);
        pasteOp.ApplyCpu(buffer);

        // Verify paste region has data
        bool hasData = false;
        for (int z = 8; z < 24 && z < _layout.Resolution.z; z++)
        {
            for (int y = 8; y < 24 && y < _layout.Resolution.y; y++)
            {
                for (int x = 20; x < 28 && x < _layout.Resolution.x; x++)
                {
                    if (buffer.DensityCpu[_layout.IndexToOffset(new Vector3Int(x, y, z))] != 0f)
                        hasData = true;
                }
            }
        }

        Assert.That(hasData, Is.True);
        VolumeClipboard.Instance.Clear();
        buffer.Dispose();
    }

    // --- Feature System ---

    [Test]
    public void FeatureDefinition_EvaluatesSphere()
    {
        var def = ScriptableObject.CreateInstance<FeatureDefinition>();
        def.shapeType = VolumeShapeType.Sphere;
        def.sphereRadius = 1f;

        Assert.That(def.EvaluateLocal(Vector3.zero), Is.EqualTo(-1f));
        Assert.That(def.EvaluateLocal(new Vector3(2f, 0f, 0f)), Is.EqualTo(1f));

        ScriptableObject.DestroyImmediate(def);
    }

    [Test]
    public void FeatureInstance_TransformsCorrectly()
    {
        var def = ScriptableObject.CreateInstance<FeatureDefinition>();
        def.shapeType = VolumeShapeType.Sphere;
        def.sphereRadius = 1f;

        var instance = new FeatureInstance(def, new Vector3(5f, 0f, 0f));

        // Point at (6,0,0) is 1 unit from center → on surface
        Assert.That(instance.Evaluate(new Vector3(6f, 0f, 0f)), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(instance.MaterialId, Is.EqualTo(def.defaultMaterialId));

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

        var instance = lib.CreateInstance("Box", new Vector3(1f, 0f, 0f));
        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.Position, Is.EqualTo(new Vector3(1f, 0f, 0f)));

        ScriptableObject.DestroyImmediate(lib);
        ScriptableObject.DestroyImmediate(def1);
        ScriptableObject.DestroyImmediate(def2);
    }

    // --- Pipeline Integration ---

    [Test]
    public void Pipeline_OperationFlow()
    {
        var mesher = new VoxelMesher();
        var pipeline = new VolumePipeline(_layout, mesher);
        pipeline.Initialize(new UnityMeshOutput(new Mesh()));
        ((ChunkedFlatVolumeBuffer)pipeline.Buffer).Initialize(1f);

        var op = new AddSphereOperation(Vector3.zero, 0.25f, 0);
        op.ComputeAffectedRegion(_layout);
        pipeline.ApplyOperation(op);

        Assert.That(pipeline.IsDirty, Is.True);
        pipeline.Dispose();
    }

    [Test]
    public void MesherFactory_CreatesGpuVoxel()
    {
        var mesher = MesherFactory.Create(PipelineMesherType.GpuVoxel);
        Assert.That(mesher, Is.InstanceOf<GpuVoxelMesher>());
        Assert.That(mesher.SupportsGpu, Is.True);
    }

    [Test]
    public void VoxelMesher_BuildsMesh()
    {
        var buffer = new ChunkedFlatVolumeBuffer(_layout);
        buffer.Initialize(1f);

        var addOp = new AddSphereOperation(Vector3.zero, 0.4f, 0);
        addOp.ComputeAffectedRegion(_layout);
        addOp.ApplyCpu(buffer);

        var mesher = new VoxelMesher();
        MeshingContext ctx = MeshingContext.Default(_layout);
        CpuMeshData mesh = mesher.BuildCpu(buffer, ctx);

        Assert.That(mesh.VertexCount, Is.GreaterThan(0));
        Assert.That(mesh.IndexCount, Is.GreaterThan(0));

        mesh.Dispose();
        buffer.Dispose();
    }
}
