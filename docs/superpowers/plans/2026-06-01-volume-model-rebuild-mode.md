# Volume Model Rebuild Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace overlapping rebuild toggles with one four-value dropdown while preserving the current preview-on-change default and avoiding any automatic Play Mode startup rebuild.

**Architecture:** `VolumeModel` owns a serialized `VolumeRebuildMode` enum and exposes intent methods. Editor scripts and `VolumeObject` consume those methods so the enum semantics stay centralized.

**Tech Stack:** Unity 6, C#, NUnit via Unity Test Framework

---

### Task 1: Add Mode Intent API

**Files:**
- Create: `Assets/Tests/Editor/VolumeModelRebuildModeTests.cs`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs`

Keep the tests in Unity's predefined editor assembly. The production code currently
lives in predefined `Assembly-CSharp`, which cannot be referenced from a custom
assembly definition without restructuring the project.

- [ ] **Step 1: Write failing tests for all four modes**

Create NUnit tests that instantiate `VolumeModel` and assert:

```csharp
Assert.That(model.ShouldAutoRebuildOnChange(), Is.True);
Assert.That(model.ShouldUseInteractionPreview(), Is.True);
Assert.That(model.ShouldRebuildEveryFrame(), Is.False);
```

Repeat for `OnChange`, `EveryFrame`, and `Manual` with the expected mutually exclusive semantics.

- [ ] **Step 2: Run Edit Mode tests and verify RED**

Run:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe' -batchmode -quit -projectPath . -runTests -testPlatform EditMode -testFilter VolumeModelRebuildModeTests -testResults Temp\volume-mode-tests.xml -logFile Temp\volume-mode-tests.log
```

Expected: compile failure because `VolumeRebuildMode` and intent methods do not exist.

- [ ] **Step 3: Add the enum, serialized field, and intent methods**

Add `PreviewAndOnChange`, `OnChange`, `EveryFrame`, and `Manual`. Replace the old booleans and route `Update()` through `ShouldRebuildEveryFrame()`.

- [ ] **Step 4: Run Edit Mode tests and verify GREEN**

Run the filtered Unity command again. Expected: all mode tests pass.

### Task 2: Route Editor and Transform Behavior Through the Mode API

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeObject.cs`
- Modify: `Assets/Scripts/VolumeSystem/Editor/VolumeModelEditor.cs`
- Modify: `Assets/Scripts/VolumeSystem/Editor/VolumeObjectEditor.cs`

- [ ] **Step 1: Replace inspector boolean checks**

Use `ShouldAutoRebuildOnChange()` in both custom editors and draw `rebuildMode` as the single dropdown.

- [ ] **Step 2: Gate transform rebuilds and previews**

Return early from `VolumeObject.Update()` unless `ShouldAutoRebuildOnTransformChange()` is true. Require `ShouldUseInteractionPreview()` before enabling preview behavior.

- [ ] **Step 3: Compile and run focused tests**

Run the filtered Unity test command. Expected: compilation succeeds and all mode tests pass.

### Task 3: Verify Project State

**Files:**
- Verify only

- [ ] **Step 1: Search for removed toggle references**

Run:

```powershell
rg -n "autoRebuildOnChange|rebuildEveryFrame" Assets\Scripts Assets\Tests
```

Expected: no matches.

- [ ] **Step 2: Run Unity Edit Mode tests**

Run the Unity Edit Mode suite. Expected: tests pass without compilation errors.

- [ ] **Step 3: Inspect Git diff**

Confirm the diff contains the enum dropdown, centralized intent API, editor routing, transform routing, and focused tests only.
