# Flat Octree Burst Frontier Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Accelerate Flat Octree dirty rebuilds by batching built-in SDF corner and center evaluations into Burst jobs without changing generated geometry or subtree reuse.

**Architecture:** `SdfSceneSnapshot` exports blittable built-in shape data to a focused Burst batch evaluator. `FlatOctreeVolumeBuilder` retains its serial reference path and adds depth-frontier sampling into temporary topology, followed by deterministic preorder emission so subtrees remain contiguous.

**Tech Stack:** Unity 6000.4.1f1, C#, Unity Burst 1.8.x, Unity Collections, Unity Jobs, Unity Mathematics, NUnit EditMode tests.

---

## File Structure

- Create `Assets/Scripts/VolumeSystem/Sources/BurstSdfSceneSnapshot.cs`: native shape snapshot, pure SDF formulas, ownership, and batch job.
- Create `Assets/Scripts/VolumeSystem/Builders/FlatOctreeFrontierBuilder.cs`: temporary frontier topology and preorder emission.
- Create `Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs`: evaluator parity, transforms, grids, threshold, and fallback.
- Create `Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs`: layout equality, determinism, and dirty reuse.
- Modify `Assets/Scripts/VolumeSystem/Sources/SdfSceneSnapshot.cs`: immutable internal shape export.
- Modify `Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs`: compatible snapshot access.
- Modify `Assets/Scripts/VolumeSystem/Builders/FlatOctreeVolumeBuilder.cs`: path selection, shared behavior, settings, and metrics.
- Modify `Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs`: frontier profiling output.

### Task 1: Blittable Built-In SDF Evaluator

**Files:**
- Create: `Assets/Scripts/VolumeSystem/Sources/BurstSdfSceneSnapshot.cs`
- Modify: `Assets/Scripts/VolumeSystem/Sources/SdfSceneSnapshot.cs`
- Create: `Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs`

- [ ] **Step 1: Write failing primitive and composition parity tests**

Create fixtures for transformed sphere, box, torus, hyperboloid, and grouped add/subtract/intersect scenes. Compare fixed sample points:

```csharp
[TestCase(VolumeShapeType.Sphere)]
[TestCase(VolumeShapeType.Box)]
[TestCase(VolumeShapeType.Torus)]
[TestCase(VolumeShapeType.Hyperboloid)]
public void Evaluate_MatchesManagedSnapshot(VolumeShapeType type)
{
    using SnapshotFixture fixture = SnapshotFixture.Create(type);
    Assert.That(BurstSdfSceneSnapshot.TryCreate(fixture.Managed, Allocator.TempJob, out BurstSdfSceneSnapshot burst), Is.True);
    using (burst)
    {
        foreach (Vector3 point in SnapshotFixture.SamplePoints)
            Assert.That(burst.Evaluate((float3)point), Is.EqualTo(fixture.Managed.Evaluate(point)).Within(1e-5f));
    }
}
```

- [ ] **Step 2: Run tests and verify the type is missing**

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'C:\Users\tgent\sdf-geometry-generation' -runTests -testPlatform EditMode -testFilter BurstSdfSceneSnapshotTests -testResults 'C:\Users\tgent\sdf-geometry-generation\Temp\BurstSdfSceneSnapshotTests.xml' -quit
```

Expected: compilation fails because `BurstSdfSceneSnapshot` is undefined.

- [ ] **Step 3: Expose captured data without mutable public arrays**

Add to `SdfSceneSnapshot`:

```csharp
internal Matrix4x4 RootLocalToWorld => _rootLocalToWorld;
internal ReadOnlySpan<ShapeData> AddShapes => _addShapes;
internal ReadOnlySpan<ShapeData> SubtractShapes => _subtractShapes;
internal ReadOnlySpan<ShapeData> IntersectShapes => _intersectShapes;
```

- [ ] **Step 4: Implement native records and pure formulas**

Define `BurstSdfShape` using only `float4x4`, `float3`, scalars, integers, and bytes. Define `BurstSdfSceneSnapshot : IDisposable` owning three `NativeArray<BurstSdfShape>` arrays. `TryCreate` returns false for null or `HasUnsupportedShapes`; successful creation copies every captured field and matrix element. Implement sphere, box, torus, hyperboloid, all composition groups, and `Dispose` with `Unity.Mathematics.math` only.

Use this exact composition order:

```csharp
float result = float.PositiveInfinity;
for (int i = 0; i < AddShapes.Length; i++)
    result = math.min(result, EvaluateWorld(AddShapes[i], worldPoint));
for (int i = 0; i < SubtractShapes.Length; i++)
    result = math.max(result, -EvaluateWorld(SubtractShapes[i], worldPoint));
for (int i = 0; i < IntersectShapes.Length; i++)
    result = math.max(result, EvaluateWorld(IntersectShapes[i], worldPoint));
return result;
```

- [ ] **Step 5: Run focused and existing composition tests**

Run Step 2 with `-testFilter "BurstSdfSceneSnapshotTests|SceneCompositeSDFTests"`. Expected: zero failures and no native leak warnings.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Scripts/VolumeSystem/Sources/BurstSdfSceneSnapshot.cs Assets/Scripts/VolumeSystem/Sources/SdfSceneSnapshot.cs Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs
git commit -m "Add Burst-compatible built-in SDF snapshot"
```

### Task 2: Grid Parity and Fallback

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Sources/BurstSdfSceneSnapshot.cs`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs`
- Modify: `Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs`

- [ ] **Step 1: Add failing tests for every grid and custom assets**

```csharp
[TestCase(VolumeGridType.Global)]
[TestCase(VolumeGridType.Sphere)]
[TestCase(VolumeGridType.Torus)]
[TestCase(VolumeGridType.Hyperboloid)]
public void Evaluate_MatchesManagedGridCutter(VolumeGridType type)
{
    using SnapshotFixture fixture = SnapshotFixture.CreateGrid(type);
    Assert.That(BurstSdfSceneSnapshot.TryCreate(fixture.Managed, Allocator.TempJob, out BurstSdfSceneSnapshot burst), Is.True);
    using (burst)
    {
        foreach (Vector3 point in SnapshotFixture.GridPoints)
            Assert.That(burst.Evaluate((float3)point), Is.EqualTo(fixture.Managed.Evaluate(point)).Within(2e-5f));
    }
}

[Test]
public void TryCreate_RejectsCustomAsset()
{
    using SnapshotFixture fixture = SnapshotFixture.Create(VolumeShapeType.CustomAsset);
    Assert.That(BurstSdfSceneSnapshot.TryCreate(fixture.Managed, Allocator.TempJob, out BurstSdfSceneSnapshot burst), Is.False);
    Assert.That(burst.IsCreated, Is.False);
}
```

- [ ] **Step 2: Run tests and confirm grid parity is incomplete**

Run Task 1 Step 2. Expected: the new grid tests fail.

- [ ] **Step 3: Implement all cutter formulas and composer access**

Mirror managed width/depth clamps, repeat centering, angular calculations, shell combination, global-world transform, and axis flags exactly. Add:

```csharp
public bool TryGetBuiltInSnapshot(out SdfSceneSnapshot snapshot)
{
    if (_snapshot == null)
        RebuildComposition();
    snapshot = _snapshot;
    return snapshot != null && !snapshot.HasUnsupportedShapes;
}
```

- [ ] **Step 4: Run parity tests**

Expected: primitive, composition, transformed grid, and custom fallback tests all pass.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/VolumeSystem/Sources/BurstSdfSceneSnapshot.cs Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs
git commit -m "Verify Burst SDF grid parity and fallback"
```

### Task 3: Synchronous Burst Batch Runner

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Sources/BurstSdfSceneSnapshot.cs`
- Modify: `Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs`

- [ ] **Step 1: Add failing threshold tests**

```csharp
[TestCase(31, false)]
[TestCase(32, true)]
public void EvaluateBatch_UsesThreshold(int count, bool expectedJob)
{
    using SnapshotFixture fixture = SnapshotFixture.Create(VolumeShapeType.Sphere);
    Assert.That(BurstSdfSceneSnapshot.TryCreate(fixture.Managed, Allocator.TempJob, out BurstSdfSceneSnapshot burst), Is.True);
    using (burst)
    using (NativeArray<float3> positions = CreatePositions(count, Allocator.TempJob))
    using (NativeArray<float> values = new NativeArray<float>(count, Allocator.TempJob))
    {
        BurstSdfBatchResult result = burst.EvaluateBatch(positions, values, 32);
        Assert.That(result.UsedJob, Is.EqualTo(expectedJob));
        Assert.That(result.SampleCount, Is.EqualTo(count));
    }
}
```

- [ ] **Step 2: Run and verify missing batch API failure**

Expected: compilation fails for `BurstSdfBatchResult` and `EvaluateBatch`.

- [ ] **Step 3: Implement job and serial branch**

Add `[BurstCompile] EvaluateSdfBatchJob : IJobParallelFor` with read-only positions/shapes and write-only values. Below the threshold, loop synchronously. At or above it, schedule with inner-loop batch count 32 and immediately `Complete()`. Return:

```csharp
public readonly struct BurstSdfBatchResult
{
    public readonly bool UsedJob;
    public readonly int SampleCount;
    public readonly long ElapsedTicks;
}
```

Validate equal array lengths and finite outputs. Throw `InvalidOperationException` on invalid output so the builder can fall back before publishing a volume.

- [ ] **Step 4: Run snapshot tests**

Expected: both branches pass numerical comparisons and Unity reports no leaked collections.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/VolumeSystem/Sources/BurstSdfSceneSnapshot.cs Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs
git commit -m "Add Burst SDF batch evaluation job"
```

### Task 4: Frontier Topology and Preorder Emission

**Files:**
- Create: `Assets/Scripts/VolumeSystem/Builders/FlatOctreeFrontierBuilder.cs`
- Modify: `Assets/Scripts/VolumeSystem/Builders/FlatOctreeVolumeBuilder.cs`
- Create: `Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs`

- [ ] **Step 1: Write failing serial/frontier equality test**

Build a sphere/subtractor fixture with `useBurstFrontier` false and true. Compare layout count, centers, sizes, coords, child masks, flags, first-child indices, corner values, surface vertices, and normals:

```csharp
[TestCase(2)]
[TestCase(4)]
[TestCase(6)]
public void Build_BurstFrontierMatchesSerialLayout(int maxDepth)
{
    using FlatOctreeFixture fixture = FlatOctreeFixture.Create(maxDepth);
    FlatOctreeLayout serial = fixture.Build(false).FlatLayout;
    FlatOctreeLayout frontier = fixture.RebuildFromCleanState(true).FlatLayout;
    AssertLayoutsEqual(serial, frontier, 2e-5f);
}
```

- [ ] **Step 2: Run and verify missing option failure**

Run the Unity command from Task 1 with `-testFilter FlatOctreeBurstFrontierTests`. Expected: compilation fails for `useBurstFrontier`.

- [ ] **Step 3: Add temporary topology and shared builder operations**

Add `[HideInInspector] public bool useBurstFrontier = true`. Make `CornerSamples` and narrowly scoped classification/cache/emission methods internal so both paths invoke identical logic. Define in the new file:

```csharp
internal struct PendingNode
{
    public Vector3 Center;
    public Vector3 Size;
    public int Depth;
    public int PreviousNodeIndex;
    public int FirstPendingChild;
    public int ReusedPreviousRoot;
    public byte ChildMask;
    public byte Flags;
    public FlatOctreeVolumeBuilder.CornerSamples Corners;
}
```

- [ ] **Step 4: Implement depth-frontier sampling**

For each depth, first classify known corners and batch only required centers. Finalize subdivision decisions, then gather the 19 non-parent points from each subdividing node's 3x3x3 lattice. Deduplicate centers by existing quantized center key and corners by `Vector3Int`. Insert results through existing cache/accounting methods, then create children in octant order `(x << 2) | (y << 1) | z`.

Reused subtrees become pending references and are neither sampled nor expanded.

- [ ] **Step 5: Emit final nodes in preorder**

Implement recursive preorder emission from pending topology. Append each parent before children; set `FirstChildIndex` to the first emitted child. Resolve reused references by calling the existing contiguous previous-subtree copy. Compute max-depth surface vertices/normals through the unchanged serial method during emission.

- [ ] **Step 6: Select the compatible path safely**

Use frontier only when source is `VolumeSceneComposer`, `TryGetBuiltInSnapshot` succeeds, and native creation succeeds. Own the native snapshot in `try/finally`. On setup/evaluation failure, clear partial per-build topology/output and invoke the existing `BuildNode` path once; retain prepared persistent caches.

- [ ] **Step 7: Run equality tests**

Expected: serial and frontier layouts match at all test depths and `EnsureRuntimeCache()` succeeds, proving subtree contiguity.

- [ ] **Step 8: Commit**

```powershell
git add Assets/Scripts/VolumeSystem/Builders/FlatOctreeFrontierBuilder.cs Assets/Scripts/VolumeSystem/Builders/FlatOctreeVolumeBuilder.cs Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs
git commit -m "Batch Flat Octree sampling by frontier"
```

### Task 5: Dirty Reuse, Determinism, and Fallback

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Builders/FlatOctreeFrontierBuilder.cs`
- Modify: `Assets/Scripts/VolumeSystem/Builders/FlatOctreeVolumeBuilder.cs`
- Modify: `Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs`

- [ ] **Step 1: Write failing dirty-reuse and deterministic tests**

```csharp
[Test]
public void DirtyBuild_ReusesUnaffectedSubtreesAndMatchesCleanSerialBuild()
{
    using FlatOctreeFixture fixture = FlatOctreeFixture.CreateMovableSphere();
    fixture.Build(true);
    Bounds dirty = fixture.MoveShapeAndGetDirtyBounds(new Vector3(0.15f, 0f, 0f));
    fixture.Builder.PreparePersistentCrossingCache(true, dirty);
    FlatOctreeLayout frontier = fixture.Build(true).FlatLayout;
    Assert.That(fixture.Builder.LastBuildStats.reusedSubtreeCount, Is.GreaterThan(0));
    FlatOctreeLayout serial = fixture.RebuildFromCleanState(false).FlatLayout;
    AssertLayoutsEqual(serial, frontier, 2e-5f);
}
```

Also build the same frontier twice from clean state and require exact array equality. Add a custom-asset fixture and require valid serial output with `frontierUsed == false`.

- [ ] **Step 2: Run tests and locate ordering/reuse mismatch**

Expected: a new test fails until previous-child indices and reused emission match serial traversal.

- [ ] **Step 3: Preserve previous-child mapping and reset semantics**

Propagate previous child indices using existing `ChildMask` and `SubtreeSize`. Before fallback, reset pending nodes, output nodes, surface counters, and per-build metrics, but do not discard prepared persistent corner, center, or crossing cache contents.

- [ ] **Step 4: Run complete EditMode suite**

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'C:\Users\tgent\sdf-geometry-generation' -runTests -testPlatform EditMode -testResults 'C:\Users\tgent\sdf-geometry-generation\Temp\EditModeResults.xml' -quit
```

Expected: zero failed tests, Burst compiler errors, and collection leak warnings.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/VolumeSystem/Builders/FlatOctreeFrontierBuilder.cs Assets/Scripts/VolumeSystem/Builders/FlatOctreeVolumeBuilder.cs Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs
git commit -m "Preserve dirty subtree reuse in Burst frontier builds"
```

### Task 6: Profiling and Benchmark Acceptance

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Builders/FlatOctreeVolumeBuilder.cs`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs`
- Modify: `Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs`

- [ ] **Step 1: Write failing metric assertions**

```csharp
Assert.That(stats.frontierUsed, Is.True);
Assert.That(stats.frontierBatchCount, Is.GreaterThan(0));
Assert.That(stats.frontierSampleCount, Is.GreaterThan(0));
Assert.That(stats.frontierJobSampleCount + stats.frontierSerialSampleCount, Is.EqualTo(stats.frontierSampleCount));
Assert.That(stats.frontierPreparationMs, Is.GreaterThanOrEqualTo(0d));
Assert.That(stats.frontierEvaluationMs, Is.GreaterThanOrEqualTo(0d));
Assert.That(stats.frontierInsertionMs, Is.GreaterThanOrEqualTo(0d));
```

- [ ] **Step 2: Run and verify missing metric failure**

Expected: compilation fails for new `BuildStats` members.

- [ ] **Step 3: Add stable profiling fields and log output**

Track preparation/deduplication, evaluation, insertion, batches, all samples, serial samples, and job samples. Preserve all existing timing labels. Append:

```text
frontier(used=True, batches=7, samples=12345, jobSamples=12288, serialSamples=57, prep=0.00 ms, eval=0.00 ms, insert=0.00 ms)
```

Use `const int MinBurstBatchSize = 32` instead of a literal in traversal code.

- [ ] **Step 4: Run full tests and inspect `Editor.log`**

Expected: zero test failures, `error CS` entries, Burst exceptions, and native leak warnings from changed files.

- [ ] **Step 5: Run visual and performance acceptance**

Run the existing Dirty Move Benchmark three times with the saved baseline scene/settings. Compare medians for model total, flat build, recursive/frontier, crossing, and GC.

Accept only when there are no chunk-border holes, rebuilt/reused resolution mismatches, or changed rotated-box/cutter geometry, and median Flat Octree dirty-build time improves without material GC regression. If it regresses, set `useBurstFrontier` default to false while retaining the isolated evaluator and tests.

- [ ] **Step 6: Commit accepted profiling/default**

```powershell
git add Assets/Scripts/VolumeSystem/Builders/FlatOctreeVolumeBuilder.cs Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs
git commit -m "Profile Burst Flat Octree frontier builds"
```

### Task 7: Final Verification

**Files:**
- Verify only.

- [ ] **Step 1: Verify repository scope**

```powershell
git diff --check HEAD~6..HEAD
git status --short
```

Expected: no whitespace failures and only pre-existing unrelated untracked files remain.

- [ ] **Step 2: Run the complete EditMode suite again**

Use Task 5 Step 4. Expected: Unity exits with code 0 and test XML reports zero failures.

- [ ] **Step 3: Report benchmark evidence**

Report three-run medians and the number of serial/job frontier samples. Do not claim a speedup unless the median demonstrates it.
