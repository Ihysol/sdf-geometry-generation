# Burst Chunk-local Rebuild Implementation Plan

**For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement plan task-by-task. Steps use checkbox (`- [ ]`) syntax tracking.

**Goal:** Improve SDF geometry rebuild performance by enabling the existing Burst-capable flat octree build path for chunk-local rebuilds before considering a larger meshing rewrite.

**Architecture:** Keep `VolumeModel` as the orchestrator and `VolumeMeshRenderer` as the chunk renderer. Reuse `FlatOctreeVolumeBuilder`'s existing Burst snapshot/frontier/pre-fill support inside chunk-local builds, while retaining the current C# fallback when a scene contains unsupported custom SDF shapes.

**Tech Stack:** Unity 6000.4, C#, NUnit EditMode tests, Unity Jobs/Burst, Unity.Mathematics, existing `BurstSdfSceneSnapshot` and `FlatOctreeVolumeBuilder`.

---

### Task 1: Make Chunk-local Flat Builder Burst-aware

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Rendering/VolumeMeshRenderer.cs`
- Test: `Assets/Tests/Editor/VolumeMeshRendererClusterTests.cs`

- [x] **Step 1: Write failing test**

Add an EditMode test proving the chunk-local flat builder copies Burst flags from the model template instead of forcing them off. Use reflection to call the private helper if it remains private, or a small internal helper if introduced.

```csharp
[Test]
public void ChunkLocalFlatBuilderSettings_PreserveBurstFlagsFromTemplate()
{
    GameObject root = new GameObject("burst-chunk-test");
    try
    {
        VolumeModel model = root.AddComponent<VolumeModel>();
        model.dataStructure = VolumeDataStructure.Octree;
        model.storageMode = VolumeStorageMode.Flat;
        model.octreeMesherType = OctreeMesherType.DualContouring;
        model.octreeSampler.flatBuilder.useBurstPreFill = true;
        model.octreeSampler.flatBuilder.useBurstFrontier = true;

        FlatOctreeVolumeBuilder builder = VolumeMeshRenderer.CreateChunkLocalFlatBuilderForTests(
            model,
            new Bounds(Vector3.zero, Vector3.one),
            new OctreeVolume(null, new Bounds(Vector3.zero, Vector3.one), 1, 0, 0, null, Vector3.zero, Vector3.one));

        Assert.That(builder.useBurstPreFill, Is.True);
        Assert.That(builder.useBurstFrontier, Is.True);
    }
    finally
    {
        Object.DestroyImmediate(root);
    }
}
```

- [ ] **Step 2: Run test verify fails**

Run:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\tgent\workspaces\sdf-geometry-generation' -runTests -testPlatform EditMode -testFilter VolumeMeshRendererClusterTests.ChunkLocalFlatBuilderSettings_PreserveBurstFlagsFromTemplate -testResults 'C:\Users\tgent\workspaces\sdf-geometry-generation\Temp\BurstChunkLocalTests.xml' -quit
```

Expected: fail because `CreateChunkLocalFlatBuilderForTests` does not exist or the flags are forced to `false`.

- [x] **Step 3: Implement minimal code**

Extract chunk-local builder construction into a helper and copy `useBurstPreFill` and `useBurstFrontier` from `model.octreeSampler.flatBuilder`.

```csharp
internal static FlatOctreeVolumeBuilder CreateChunkLocalFlatBuilderForTests(
    VolumeModel model,
    Bounds buildBounds,
    OctreeVolume globalVolume)
{
    return CreateChunkLocalFlatBuilder(model, buildBounds, globalVolume);
}

private static FlatOctreeVolumeBuilder CreateChunkLocalFlatBuilder(
    VolumeModel model,
    Bounds buildBounds,
    OctreeVolume globalVolume)
{
    FlatOctreeVolumeBuilder template = model.octreeSampler.flatBuilder;
    ChunkLocalBuildGrid buildGrid = GetChunkLocalBuildGrid(template, globalVolume, buildBounds);

    return new FlatOctreeVolumeBuilder
    {
        center = buildGrid.Bounds.center,
        size = buildGrid.Bounds.size,
        boundsPadding = 0f,
        maxDepth = buildGrid.MaxDepth,
        minDepth = Mathf.Clamp(template.minDepth, 0, buildGrid.MaxDepth),
        suppressBuildLog = true,
        edgeRefinementSteps = template.edgeRefinementSteps,
        sampleCacheDirtyPaddingCells = 0f,
        profileRecursiveParts = false,
        useBurstPreFill = template.useBurstPreFill,
        useBurstFrontier = template.useBurstFrontier
    };
}
```

- [ ] **Step 4: Run test verify passes**

Run the same focused EditMode test command. Expected: pass.

- [x] **Step 5: Run diagnostics**

Run:

```powershell
tokensave_diagnostics workspace
```

Expected: zero compile diagnostics.

### Task 2: Baseline and Guardrails for Larger Jobs/Burst Rewrite

**Files:**
- Modify later: `Assets/Scripts/VolumeSystem/Rendering/VolumeMeshRenderer.cs`
- Modify later: `Assets/Scripts/VolumeSystem/Rendering/Chunking/OctreeChunkMesher.cs`
- Create later if needed: `Assets/Scripts/VolumeSystem/Rendering/Chunking/FlatDualContouringChunkJob.cs`

- [ ] **Step 1: Use existing profiling fields**

Measure `rendererChunkLocalBuildMs`, `rendererChunkMeshBuildMs`, and `rendererChunkApplyMeshMs` before adding new jobified meshing code.

- [ ] **Step 2: Only jobify the dominant phase**

If `chunkLocalBuildMs` dominates, extend Burst flat-octree build first. If `chunkMeshBuildMs` dominates, introduce a data-only mesh job. If `chunkApplyMeshMs` dominates, reduce mesh apply churn instead of moving more work to Burst.

- [ ] **Step 3: Preserve fallback behavior**

Any new job path must fall back to the current managed path when `VolumeSceneComposer.TryGetBuiltInSnapshot` reports unsupported shapes.
