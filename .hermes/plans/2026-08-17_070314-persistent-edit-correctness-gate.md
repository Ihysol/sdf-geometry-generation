# Persistent Edit Correctness Gate Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Establish a green, durable, transaction-based persistent-edit path that survives partial/full rebuilds and pipeline replacement before Build Tickets or typed scheduling are introduced.

**Architecture:** Move edit ownership from disposable `VolumePipeline` into a processor-owned `VolumeEditDocument`, inject it through a small store boundary, and rematerialize affected regions from Authoring Composition plus active transactions. Keep the existing flat buffer and mesh scheduler intact behind migration seams; do not implement ADR-010 Build Tickets in this milestone.

**Tech Stack:** Unity 6000.4.1f1, C#, NUnit EditMode tests, Unity.Collections, existing Burst sampling and `ChunkedFlatVolumeBuffer`.

---

## Scope and guardrails

- Follow strict RED → verify RED → GREEN → verify GREEN → REFACTOR for every behavior change.
- Use a separate Unity project copy for batch tests; never terminate the user's main Unity instance.
- Do not modify `Packages/packages-lock.json`; its current `3.3.0 → 3.2.0` change is outside this plan.
- Do not implement Build Tickets, typed scheduler stages, staged hot-swap, real checkpoint compaction, ScriptableObject storage, or sparse DAG persistence in this gate.
- Preserve script `.meta` files and GUIDs.
- Do not commit without explicit user confirmation. Suggested commit boundaries are listed, but execution must pause for approval before each commit.
- Report Core Volume, Legacy/Adaptive, and Package/Environment gates separately.

## Current verified baseline

Fresh isolated EditMode run on 2026-08-17:

```text
Total: 83
Passed: 73
Failed: 10
```

Core failures to classify/fix in this gate:

- `PipelineTests.SubtractSphereOperation_ModifiesDensity`
- `VolumePipelineTests.SubtractSphere_AppliesDensity`
- `PipelineTests.CopyPasteOperation_FullCycle`
- `PipelineTests.VoxelMesher_BuildsMesh`
- `VolumeProcessorAddObjectTests.AddIntersectObject_WhenBufferAlreadyExists_UsesFullRebuild`
- `VolumeProcessorAddObjectTests.AddObject_WhenBufferAlreadyExists_UsesPartialRebuild`

Legacy/Adaptive failures remain separately visible:

- two `DualContouringOctreeMesherCacheTests`
- two `VolumeCoreUtilityTests` flat-octree cache/profiling tests

## Batch-test template

Use an isolated copy such as:

```text
C:\Users\tgent\AppData\Local\Temp\hermes-sdf-correctness-gate
```

Run a focused fixture:

```bash
export MSYS2_ARG_CONV_EXCL='*'
'/c/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe' \
  -batchmode -nographics \
  -projectPath 'C:\Users\tgent\AppData\Local\Temp\hermes-sdf-correctness-gate' \
  -runTests -testPlatform EditMode \
  -testFilter 'PersistentEditCorrectnessTests' \
  -testResults 'C:\Users\tgent\AppData\Local\Temp\persistent-edit-results.xml' \
  -logFile 'C:\Users\tgent\AppData\Local\Temp\persistent-edit.log'
```

Expected results must be parsed from NUnit XML; Unity process exit alone is not sufficient evidence.

---

### Task 1: Establish explicit Core Volume baseline fixtures

**Objective:** Separate active flat-buffer/pipeline regressions from Legacy/Adaptive failures and capture the intended Core Gate in one repeatable filter.

**Files:**
- Create: `Assets/Tests/Editor/CoreVolumeGateTests.cs`
- Modify only if classification shows stale setup: `Assets/Tests/Editor/PipelineTests.cs`
- Modify only if classification shows stale setup: `Assets/Tests/Editor/VolumePipelineTests.cs`
- Inspect: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`

**Step 1: Write failing characterization tests**

Add focused tests or wrappers for:

- filled SDF minus sphere produces positive density at sphere center;
- copy source region actually intersects known authored density and paste reproduces Density/MaterialId;
- VoxelMesher fixture initializes outside density explicitly positive;
- second Add object leaves two registered objects;
- Intersect triggers a full rebuild independent of the exact historical chunk count.

Each test must assert behavior, not private implementation.

**Step 2: Verify RED**

Run only `CoreVolumeGateTests` plus the two affected existing fixtures. Expected: failures matching current operation/composition defects, not compile errors.

**Step 3: Classify failures**

Document in test comments only when necessary:

- implementation regression;
- stale/invalid fixture setup;
- assertion coupled to obsolete scheduler count.

Do not weaken a valid behavioral assertion merely to make the suite green.

**Step 4: Verify the gate report**

Expected: Core and Legacy failure lists can be produced independently.

**Suggested commit boundary:** `test: define core volume correctness gate` — pause for approval.

---

### Task 2: Correct SDF subtraction semantics

**Objective:** Make CPU sphere subtraction use the repository's negative-inside SDF convention.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Core/Pipeline/SubtractSphereOperation.cs:34-58`
- Verify GPU parity only; do not redesign GPU ownership: `Assets/Scripts/VolumeSystem/Core/Pipeline/GpuOperationDispatcher.cs`
- Test: `Assets/Tests/Editor/CoreVolumeGateTests.cs`
- Test: `Assets/Tests/Editor/PipelineTests.cs`
- Test: `Assets/Tests/Editor/VolumePipelineTests.cs`

**Step 1: Confirm existing RED tests**

Run the two SubtractSphere tests. Expected: center remains `-1` instead of becoming positive.

**Step 2: Implement minimal GREEN**

For a sphere SDF `d = distance - radius` and current field `a`, subtraction uses:

```text
max(a, -d)
```

Clamp iteration to the valid affected region without changing unrelated operation APIs.

**Step 3: Verify GREEN**

Run both SubtractSphere tests and Core Gate. Expected: all subtraction assertions pass.

**Step 4: Refactor only after green**

Share CPU/GPU SDF sign semantics only if an existing helper can do so without broad redesign.

**Suggested commit boundary:** `fix: correct subtract sphere SDF composition` — pause for approval.

---

### Task 3: Restore Add/Intersect composition behavior without dual undo ownership

**Objective:** Fix active Add/Intersect regressions while aligning Editor Authoring with ADR-007.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs:393-476`
- Modify: `Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs:131-220`
- Modify: `Assets/Scripts/VolumeSystem/Editor/VolumeObjectEditor.cs`
- Modify or retire from Editor path: `Assets/Scripts/VolumeSystem/Core/Undo/CompositionCommand.cs`
- Modify or retire from Editor path: `Assets/Scripts/VolumeSystem/Core/Undo/CommandStack.cs`
- Test: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`
- Create: `Assets/Tests/Editor/VolumeAuthoringUndoTests.cs`

**Step 1: Write RED behavior tests**

Prove:

- adding a second object yields two registry entries;
- Add/Subtract with an existing valid buffer queues partial work;
- Intersect requests full-volume rebuild semantics;
- remove captures affected bounds before destruction;
- redo of Editor Add/Remove/Clear is owned by Unity Undo, not a parallel CommandStack.

Avoid asserting an exact full-rebuild chunk count when layout/halo legitimately changes it; assert full-vs-partial semantics and total dirty coverage instead.

**Step 2: Verify RED**

Expected: current dual-history path or registration order fails at least one new test.

**Step 3: Implement minimal GREEN**

- Use `Undo.RegisterCreatedObjectUndo` for Editor creation.
- Use `Undo.DestroyObjectImmediate` for Editor deletion.
- Use `Undo.RecordObject`/serialized APIs for registry and parameters.
- Subscribe once to `Undo.undoRedoPerformed` to rebuild affected state safely.
- Remove Editor recording of the same action into custom `CommandStack`.
- Keep any future runtime stack outside `#if UNITY_EDITOR` paths and out of scope unless needed to compile.

**Step 4: Verify GREEN**

Run `VolumeProcessorAddObjectTests` and `VolumeAuthoringUndoTests`.

**Step 5: Refactor**

Remove dead Editor shortcut interception and misleading comments only after tests pass.

**Suggested commit boundary:** `fix: restore single-owner authoring undo and composition rebuilds` — pause for approval.

---

### Task 4: Introduce the pipeline-independent Volume Edit Document

**Objective:** Create durable in-memory edit ownership without changing replay behavior yet.

**Files:**
- Create: `Assets/Scripts/VolumeSystem/Edit/VolumeEditDocument.cs`
- Create: `Assets/Scripts/VolumeSystem/Edit/EditTransaction.cs`
- Create: `Assets/Scripts/VolumeSystem/Edit/Storage/IVolumeEditStore.cs`
- Create: `Assets/Scripts/VolumeSystem/Edit/Storage/InMemoryVolumeEditStore.cs`
- Modify: `Assets/Scripts/VolumeSystem/Edit/PersistentEditLayer.cs`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs`
- Modify: `Assets/Scripts/VolumeSystem/Core/Pipeline/VolumePipeline.cs`
- Create: `Assets/Tests/Editor/VolumeEditDocumentTests.cs`

**Step 1: Write RED tests**

Prove:

- a document owns ordered transactions and a cursor;
- an in-memory store saves and loads by Document ID;
- a pipeline receives an existing document rather than constructing its own;
- disposing/recreating a pipeline preserves the same document and active transaction count;
- creating a new transaction after undo truncates redo history.

**Step 2: Verify RED**

Expected: document/store types do not exist or pipeline replaces edit state.

**Step 3: Implement minimal GREEN**

Use plain C# data with explicit schema/document revision. Do not add ScriptableObject storage yet. Adapt `PersistentEditLayer` behind or into the document with the smallest migration seam.

**Step 4: Verify GREEN**

Run `VolumeEditDocumentTests` and existing pipeline fixtures.

**Step 5: Refactor**

Make ownership names match `CONTEXT.md`; remove `new PersistentEditLayer()` from `VolumePipeline.Initialize`.

**Suggested commit boundary:** `feat: move persistent edits into volume edit document` — pause for approval.

---

### Task 5: Add stable VolumeObject identities and anchor resolution

**Objective:** Resolve World/Processor/Object anchors deterministically without scene lookup inside operations.

**Files:**
- Create: `Assets/Scripts/VolumeSystem/Edit/VolumeObjectIdentity.cs` or add a focused serialized identity field to `Assets/Scripts/VolumeSystem/Composition/VolumeObject.cs`
- Create: `Assets/Scripts/VolumeSystem/Edit/EditReplayContext.cs`
- Create: `Assets/Scripts/VolumeSystem/Edit/IEditAnchorResolver.cs`
- Modify: `Assets/Scripts/VolumeSystem/Edit/EditAnchor.cs`
- Modify: `Assets/Scripts/VolumeSystem/Edit/PersistentEditOperation.cs`
- Modify: `Assets/Scripts/VolumeSystem/Edit/Operations/CarveOperation.cs`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs`
- Create: `Assets/Tests/Editor/EditAnchorReplayTests.cs`

**Step 1: Write RED tests**

Prove:

- World Anchor leaves bounds unchanged;
- Processor Anchor transforms local bounds exactly once;
- Object Anchor resolves by stable serialized GUID;
- missing Object Anchor suspends the transaction;
- restoring the same GUID reactivates it;
- operation replay does not perform a second null-transform resolution;
- duplicate GUID detection fails closed until controlled remap.

**Step 2: Verify RED**

Expected: Processor replay currently fails/null-dereferences or Object Anchor remains unresolved.

**Step 3: Implement minimal GREEN**

Create immutable replay context and registry-backed resolver. Resolve an operation region once before applying it. Preserve existing World carve behavior.

**Step 4: Verify GREEN**

Run `EditAnchorReplayTests` and `VolumeEditDocumentTests`.

**Step 5: Refactor**

Remove `Transform` lookup responsibility from operation classes.

**Suggested commit boundary:** `feat: resolve persistent edit anchors through replay context` — pause for approval.

---

### Task 6: Preserve committed edits through partial/full rebuild and pipeline replacement

**Objective:** Prove the layered state model end to end.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Core/Pipeline/VolumePipeline.cs:85-216`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs:229-367,478-532`
- Test: `Assets/Tests/Editor/PersistentEditCorrectnessTests.cs`

**Step 1: Write first RED tracer test**

Scenario:

1. build one SDF object;
2. commit one World-anchored carve transaction;
3. partial rebuild overlapping carve;
4. assert carved Density remains empty.

Verify RED for the intended missing path, then implement only enough to pass.

**Step 2: Write second RED tracer test**

Explicit full rebuild must preserve the carve. Expected current failure because `RebuildModel()` does not pass replay context/document state.

Implement only enough to pass and rerun both tests.

**Step 3: Write third RED tracer test**

Resize/recreate the pipeline, then rebuild. The same document transaction must replay into the new buffer. Expected current failure because pipeline replacement loses the layer.

Implement document injection on resize and rerun all three tests.

**Step 4: Add lifecycle coverage**

Dispose/reinitialize without deleting the processor-owned document; verify replay remains deterministic.

**Step 5: Verify GREEN**

Run `PersistentEditCorrectnessTests`, `VolumeProcessorAddObjectTests`, and Core Gate.

**Suggested commit boundary:** `fix: preserve persistent edits across all rebuild paths` — pause for approval.

---

### Task 7: Implement transaction-cursor undo/redo by regional rematerialization

**Objective:** Make one brush stroke one undo step and support lossy operations without mathematical inverses.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Edit/EditTransaction.cs`
- Modify: `Assets/Scripts/VolumeSystem/Edit/VolumeEditDocument.cs`
- Modify: `Assets/Scripts/VolumeSystem/Edit/PersistentEditLayer.cs`
- Modify: `Assets/Scripts/VolumeSystem/Edit/Editor/VolumeCarveTool.cs`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs`
- Test: `Assets/Tests/Editor/VolumeEditDocumentTests.cs`
- Test: `Assets/Tests/Editor/PersistentEditCorrectnessTests.cs`

**Step 1: Write RED transaction test**

Multiple carve samples committed as one transaction must increment undo count once and expose union affected bounds.

**Step 2: Verify RED, implement GREEN**

Add explicit begin/add/commit or immutable transaction construction. Do not add a transaction per drag sample.

**Step 3: Write RED undo test**

Undo moves cursor and rematerializes transaction bounds from Authoring Base plus remaining active transactions. Verify carved density restores.

**Step 4: Implement GREEN**

Do not call `Inverse()`. Trigger regional rebuild through document cursor change.

**Step 5: Write RED redo test**

Redo moves cursor forward and reapplies the same transaction. Use an explicit requested direction; do not infer direction from `CanUndo`/`CanRedo` on `ValidateCommand`.

**Step 6: Implement GREEN and verify**

Bridge Editor shortcuts without intercepting Authoring Unity Undo ownership.

**Suggested commit boundary:** `feat: add transaction-based persistent edit undo redo` — pause for approval.

---

### Task 8: Make checkpoints fail-closed until real snapshots exist

**Objective:** Ensure checkpoint metadata can never suppress edit replay without restored data.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Edit/PersistentEditLayer.cs:4-9,56-109`
- Modify or create model types in: `Assets/Scripts/VolumeSystem/Edit/EditCheckpoint.cs`
- Test: `Assets/Tests/Editor/EditCheckpointTests.cs`

**Step 1: Write RED tests**

Prove:

- missing checkpoint returns `null`;
- requesting checkpoint creation without Density/Material snapshot returns false;
- failed checkpoint creation changes no replay generation state;
- stale layout/base/channel revision never skips an operation;
- region spanning multiple chunks does not use one center-chunk checkpoint for all cells.

**Step 2: Verify RED**

Expected: current `GetCheckpoint` returns a default value and `CreateCheckpoint` reports success.

**Step 3: Implement minimal GREEN**

Disable metadata-only optimization. Introduce revision-bearing model shape only as needed by tests; do not implement compaction/storage yet.

**Step 4: Verify GREEN**

Run `EditCheckpointTests` plus persistent edit fixtures.

**Suggested commit boundary:** `fix: make edit checkpoints fail closed` — pause for approval.

---

### Task 9: Expand Volume Views to core Density and Material channels

**Objective:** Support persistent Paint and valid checkpoints without coupling operations to concrete arrays.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Edit/PersistentEditOperation.cs` (`IVolumeView` migration seam)
- Modify: `Assets/Scripts/VolumeSystem/Edit/BufferAsEditView.cs`
- Create: `Assets/Scripts/VolumeSystem/Edit/VolumeChannelSchema.cs`
- Modify: `Assets/Scripts/VolumeSystem/Core/Pipeline/ChunkedFlatVolumeBuffer.cs` only for adapter support
- Create: `Assets/Tests/Editor/VolumeViewTests.cs`

**Step 1: Write RED tests**

Prove regional read/write for Density and MaterialId, bounds behavior, channel schema version, and borrowed-view lifetime rejection after buffer replacement.

**Step 2: Verify RED**

Expected: Material API/schema does not exist.

**Step 3: Implement minimal GREEN**

Keep Density/Material strongly typed. Do not implement optional custom channels yet; provide descriptor/version seams only.

**Step 4: Verify GREEN**

Run `VolumeViewTests`, operation tests, and Core Gate.

**Suggested commit boundary:** `feat: expose density and material volume views` — pause for approval.

---

### Task 10: Route user-facing operations into persistent transactions

**Objective:** Prevent Inspector operations from being silently erased by the following rebuild.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs:86-129`
- Modify: `Assets/Scripts/VolumeSystem/Edit/Editor/VolumeCarveTool.cs`
- Create or adapt persistent operations under: `Assets/Scripts/VolumeSystem/Edit/Operations/`
- Rename/mark transient APIs under: `Assets/Scripts/VolumeSystem/Core/Pipeline/IVolumeOperation.cs`
- Test: `Assets/Tests/Editor/PersistentEditCorrectnessTests.cs`

**Step 1: Write one RED vertical slice**

Inspector-style Subtract/Carve commits one persistent transaction, survives full rebuild, and increments document revision.

**Step 2: Verify RED and implement GREEN**

Migrate one operation end to end before adding more operation classes.

**Step 3: Repeat vertical slices**

One at a time:

- Fill/Add;
- Paint Material;
- Smooth;
- Paste.

For each: write RED, verify, implement minimal GREEN, verify all previous slices.

**Step 4: Mark transient path explicitly**

Add naming/API/UI evidence that transient operations are non-persistent. No ordinary Editor button may invoke them silently.

**Step 5: Verify GREEN**

Run persistent fixtures and Core Gate.

**Suggested commit boundary:** `feat: persist user-facing volume operations` — pause for approval.

---

### Task 11: Final Correctness Gate verification and documentation

**Objective:** Demonstrate the milestone is complete without claiming unrelated gates are green.

**Files:**
- Modify if implementation status changed: `docs/volume_buffer_architecture_plan_v11.md`
- Create/update handover: `.hermes/HANDOVER-persistent-edit-correctness.md`
- Do not modify: `Packages/packages-lock.json`

**Step 1: Run focused persistent-edit fixtures**

Expected: all pass.

**Step 2: Run Core Volume Gate**

Expected: all active flat-buffer/composition/scheduler/mesher/edit tests pass.

**Step 3: Run full EditMode suite**

Report exact total/passed/failed. Legacy/Adaptive failures may remain only if individually named and unchanged.

**Step 4: Run compile checks from freshly generated Unity artifacts**

Do not use stale Bee response files that reference removed sources. Record Runtime and Editor compilation independently.

**Step 5: Check leaks**

Run with Unity leak diagnostics if shutdown still reports TempJob/Persistent allocations. Attribute leaks to fixtures/paths before claiming clean output.

**Step 6: Verify repository scope**

```bash
git branch --show-current
git status --short
git diff --check
git diff --cached --check
git ls-files -u
git diff -- Packages/packages-lock.json
```

Expected: only planned source/test/doc files plus the pre-existing external package-lock change.

**Step 7: Present commit proposal**

Summarize tests and exact staged file list. Do not commit until the user explicitly approves.

---

## Files likely to change

### New

- `Assets/Scripts/VolumeSystem/Edit/VolumeEditDocument.cs`
- `Assets/Scripts/VolumeSystem/Edit/EditTransaction.cs`
- `Assets/Scripts/VolumeSystem/Edit/EditReplayContext.cs`
- `Assets/Scripts/VolumeSystem/Edit/IEditAnchorResolver.cs`
- `Assets/Scripts/VolumeSystem/Edit/VolumeChannelSchema.cs`
- `Assets/Scripts/VolumeSystem/Edit/Storage/IVolumeEditStore.cs`
- `Assets/Scripts/VolumeSystem/Edit/Storage/InMemoryVolumeEditStore.cs`
- `Assets/Tests/Editor/CoreVolumeGateTests.cs`
- `Assets/Tests/Editor/VolumeAuthoringUndoTests.cs`
- `Assets/Tests/Editor/VolumeEditDocumentTests.cs`
- `Assets/Tests/Editor/EditAnchorReplayTests.cs`
- `Assets/Tests/Editor/PersistentEditCorrectnessTests.cs`
- `Assets/Tests/Editor/EditCheckpointTests.cs`
- `Assets/Tests/Editor/VolumeViewTests.cs`

### Modified

- `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs`
- `Assets/Scripts/VolumeSystem/Composition/VolumeObject.cs`
- `Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs`
- `Assets/Scripts/VolumeSystem/Core/Pipeline/VolumePipeline.cs`
- `Assets/Scripts/VolumeSystem/Core/Pipeline/SubtractSphereOperation.cs`
- `Assets/Scripts/VolumeSystem/Edit/PersistentEditLayer.cs`
- `Assets/Scripts/VolumeSystem/Edit/PersistentEditOperation.cs`
- `Assets/Scripts/VolumeSystem/Edit/EditAnchor.cs`
- `Assets/Scripts/VolumeSystem/Edit/BufferAsEditView.cs`
- `Assets/Scripts/VolumeSystem/Edit/Operations/CarveOperation.cs`
- `Assets/Scripts/VolumeSystem/Edit/Editor/VolumeCarveTool.cs`
- `Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs`
- `Assets/Scripts/VolumeSystem/Editor/VolumeObjectEditor.cs`
- relevant existing Editor tests

## Risks and mitigations

- **Unity Undo and Edit History collide:** enforce one owner per state layer and test both paths separately.
- **Document duplication shares state accidentally:** default deep clone with atomic GUID/anchor remap.
- **Replay cost grows:** spatial indexing/checkpoints remain later optimizations; correctness precedes compaction.
- **Checkpoint data becomes stale:** fail closed on any revision mismatch.
- **Full rebuild blocks interaction:** this gate preserves current scheduling behavior; ADR-010/012 follow only after state correctness.
- **Legacy failures hide regressions:** report separate gates with exact failing test names.
- **Package resolver mutates lockfile:** never include package-lock changes in gate commits without separate user approval.

## Definition of done

- All acceptance criteria in v11 section 14 are backed by automated tests.
- Core Volume Gate is green.
- Persistent edits survive partial/full rebuild, resize, pipeline replacement, undo, and redo.
- Checkpoint stubs cannot suppress replay.
- User-facing operations are persistent by default.
- Test output reports all three quality lanes honestly.
- No Build Ticket or scheduler redesign has leaked into this milestone.
- No commit has occurred without explicit user confirmation.
