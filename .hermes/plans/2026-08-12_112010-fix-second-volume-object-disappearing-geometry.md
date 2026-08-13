# Zweites Volume-Objekt ohne Geometrieverlust Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Beim Hinzufügen eines zweiten `VolumeObject` darf die bereits sichtbare Geometrie nicht verschwinden; beide Add-Objekte müssen in Komposition, Volume-Buffer und Chunk-Mesh erhalten bleiben.

**Architecture:** Der Fix wird entlang des aktuellen Pipeline-Pfads abgesichert: `VolumeProcessor.AddObject` → `VolumeObjectRegistry.RebuildComposition` → `SdfSceneSnapshot` → `VolumePipeline.Rebuild` → `InitialBufferBuilder.BuildBurst` → `VolumeScheduler`. Zuerst reproduziert ein EditMode-Integrationstest den Fehler mit zwei Objekten. Danach werden die zwei erkannten Gefahren getrennt behoben: doppelte Full-Rebuilds beim Hinzufügen sowie der destruktive Rebuild bei Objekten außerhalb des festen Grid-Bounds. Ein notwendiges Grid-Resize wird synchron in demselben Rebuild abgeschlossen, damit die Lösung im Edit Mode nicht von einem späteren Editor-Tick abhängt.

**Tech Stack:** Unity 6000.4.1f1, C#, NUnit/Unity Test Framework, Unity Jobs/Burst, Git.

---

## Aktueller Kontext und Vergleichsergebnis

### Beobachteter Ablauf im aktuellen Unity-Log

Das aktuelle `Editor.log` zeigt den Fehlerpfad bereits sehr deutlich:

```text
[VolumeProcessor] Added Sphere (Add), total=2
[VolumeProcessor] RebuildModel called
[VolumeProcessor] Objects exceed grid bounds!
Grid: (1.3, -2.0, -1.2)..(5.3, 2.0, 2.8)
Objects: (2.1, 5.9, -0.4)..(4.6, 8.4, 2.1)
Enable autoExpand or manually increase boundsExtent.
[VolumePipeline] Rebuild full: 0 chunks, center=6.160, iso=0
[Scheduler] Chunk (...): 0 verts, 0 indices
```

Damit ist die sichtbare Geometrie nicht primär wegen einer verlorenen Registry-Referenz verschwunden: `total=2` bestätigt, dass beide Objekte registriert sind. Der vollständige Rebuild tastet aber ein Grid ab, das die Objekt-Bounds in Y überhaupt nicht enthält. Anschließend ersetzen die leeren Chunk-Ergebnisse die zuvor sichtbaren Meshes.

### Vergleich mit den anderen Branches

- `origin/pipeline-rework` enthält denselben `AddObject`-/`RebuildModel`-/`CheckBoundsFit`-Ablauf wie der aktuelle Branch. Der Fehler ist daher nicht erst durch den lokalen Rename `VolumeModel` → `VolumeProcessor` entstanden.
- `origin/master` und `origin/dev` verwenden noch die ältere Volume-Architektur. Dort wird ein neues Objekt nur registriert und danach einmal `RebuildModel()` aufgerufen.
- Die neue Pipeline hat zusätzlich ein festes `VolumeLayout`. Bei `autoExpand == false` warnt `CheckBoundsFit()`, gibt aber aktuell `true` zurück und lässt den leeren, destruktiven Rebuild weiterlaufen.
- `VolumeProcessor.AddObject()` löst aktuell außerdem zwei Full-Rebuilds aus:
  1. `CommandStack.Push(...)` ruft `OnStateChanged` auf und damit `RebuildModel()`.
  2. `AddObject()` ruft danach nochmals explizit `RebuildModel()` auf.
- Der Burst-Union-Code selbst ist für mehrere `Add`-Objekte strukturell korrekt: `CreateBurstShapes()` überträgt alle Shapes, und `BurstSdfSamplingJob.Execute()` vereinigt sie mit `Mathf.Min`. Dieser Pfad besitzt aber noch keinen Integrationstest, der zwei aufeinanderfolgend hinzugefügte Objekte bis in den Volume-Buffer verfolgt.

### Arbeitshypothese

Die Hauptursache des beobachteten Verschwindens ist:

1. Die Objekte liegen außerhalb des aktuellen Grid-Bounds.
2. `autoExpand` ist deaktiviert.
3. `CheckBoundsFit()` warnt nur, erlaubt aber trotzdem einen Full-Rebuild.
4. Dieser Full-Rebuild erzeugt leere Dichte- und Chunk-Daten und überschreibt die vorhandene Geometrie.

Der doppelte Rebuild ist ein zusätzlicher Fehler im Add-Pfad und soll im selben Fix entfernt werden, weil er das Problem zweimal auslösen und Debugging/Undo unnötig komplizieren kann.

### Annahmen / gewünschtes Verhalten

- Mit `autoExpand == true` wird das Grid auf die Gesamt-Bounds aller registrierten Objekte erweitert und **im selben Aufruf** vollständig neu aufgebaut.
- Mit `autoExpand == false` wird ein Rebuild außerhalb des Grid-Bounds abgebrochen, bevor Buffer oder Chunk-Meshes geleert werden. Die bestehende Geometrie bleibt sichtbar, und Unity meldet präzise, dass das neue Objekt erst nach Aktivierung von Auto Expand bzw. größeren Bounds dargestellt werden kann.
- Neue `VolumeProcessor`-Komponenten verwenden standardmäßig `autoExpand == true`; explizit gespeicherte Benutzerwerte bleiben respektiert.
- `AddObject()` führt genau einen Rebuild aus.
- Union, Subtract und Intersect behalten ihre bisherige Gruppierungssemantik.

---

### Task 1: Zwei-Objekt-Regression auf SDF- und Buffer-Ebene reproduzieren

**Objective:** Einen fokussierten EditMode-Test erstellen, der beweist, dass zwei nacheinander registrierte Add-Sphären gleichzeitig im Snapshot und im Pipeline-Buffer vorhanden sein müssen.

**Files:**
- Create: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`
- Create: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs.meta` (von Unity erzeugen lassen; nicht manuell mit einer bestehenden GUID kopieren)
- Read only: `Assets/Tests/Editor/SceneCompositeSDFTests.cs`
- Read only: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs`
- Read only: `Assets/Scripts/VolumeSystem/Core/Pipeline/VolumePipeline.cs`

**Step 1: Test-Fixture mit kleiner Pipeline anlegen**

Die Fixture soll pro Test ein Root-`GameObject` mit `VolumeObjectRegistry` und `VolumeProcessor` erzeugen. Für schnelle Tests:

```csharp
private GameObject _root;
private VolumeProcessor _processor;
private VolumeObjectRegistry _registry;

[SetUp]
public void SetUp()
{
    _root = new GameObject("VolumeProcessorAddObjectTests");
    _registry = _root.AddComponent<VolumeObjectRegistry>();
    _processor = _root.AddComponent<VolumeProcessor>();
    _processor.resolution = new Vector3Int(32, 32, 32);
    _processor.chunkSize = 16;
    _processor.boundsExtent = 8f;
    _processor.autoExpand = true;
    _processor.computeBackend = ComputeBackend.CPU;
}

[TearDown]
public void TearDown()
{
    if (_processor != null)
        _processor.Dispose();
    Object.DestroyImmediate(_root);
}
```

Falls `RequireComponent` die Registry bereits erzeugt, die Fixture ohne doppeltes `AddComponent` formulieren:

```csharp
_processor = _root.AddComponent<VolumeProcessor>();
_registry = _root.GetComponent<VolumeObjectRegistry>();
```

**Step 2: Fehlenden Zwei-Objekt-Integrationstest schreiben**

```csharp
[Test]
public void AddObject_Twice_PreservesBothShapesInSnapshotAndBuffer()
{
    _processor.Initialize();
    _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
    VolumeObject first = _registry.objects[0];
    first.transform.localPosition = new Vector3(-1.5f, 0f, 0f);
    first.sphereRadius = 0.75f;
    _processor.RebuildModel();

    _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
    VolumeObject second = _registry.objects[1];
    second.transform.localPosition = new Vector3(1.5f, 0f, 0f);
    second.sphereRadius = 0.75f;
    _processor.RebuildModel();

    Assert.That(_registry.objects, Has.Count.EqualTo(2));
    Assert.That(_registry.Snapshot.ShapeCount, Is.EqualTo(2));
    Assert.That(_registry.Snapshot.Evaluate(first.transform.localPosition), Is.LessThan(0f));
    Assert.That(_registry.Snapshot.Evaluate(second.transform.localPosition), Is.LessThan(0f));

    AssertBufferSampleInside(_processor.Pipeline.Buffer, first.transform.position);
    AssertBufferSampleInside(_processor.Pipeline.Buffer, second.transform.position);
}
```

`AssertBufferSampleInside` muss den Weltpunkt über `Buffer.Layout.Origin` und `CellSize` auf einen gültigen Zellenindex abbilden und `DensityCpu[index] < isoLevel` prüfen. Den nächstgelegenen Index clampen; keine festen Array-Indizes verwenden.

**Step 3: Test ausführen und Ausgangszustand dokumentieren**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter VolumeProcessorAddObjectTests.AddObject_Twice_PreservesBothShapesInSnapshotAndBuffer \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-two-object-red.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-two-object-red.log"
```

Expected: Entweder FAIL im Buffer-/Snapshot-Pfad oder PASS, falls der Fehler nur beim Out-of-Bounds-Rebuild reproduzierbar ist. Ein PASS ist hier zulässig und grenzt die Ursache auf Bounds/Rendering ein; Task 2 muss im Ausgangszustand sicher fehlschlagen.

**Step 4: Commit**

```bash
git add Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs.meta
git commit -m "test: cover two-object volume composition"
```

---

### Task 2: Destruktiven Out-of-Bounds-Rebuild reproduzieren

**Objective:** Den im `Editor.log` sichtbaren Fehler deterministisch als Test abbilden: Ein zweites Objekt außerhalb des Grid-Bounds darf die zuvor gültigen Buffer-/Mesh-Daten nicht durch leere Ergebnisse ersetzen.

**Files:**
- Modify: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`

**Step 1: Fehlenden Regressionstest für deaktiviertes Auto Expand schreiben**

```csharp
[Test]
public void RebuildModel_WhenBoundsDoNotFitAndAutoExpandIsOff_PreservesPreviousBuffer()
{
    _processor.autoExpand = false;
    _processor.Initialize();

    _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
    VolumeObject first = _registry.objects[0];
    first.transform.localPosition = Vector3.zero;
    first.sphereRadius = 0.75f;
    _processor.RebuildModel();

    float[] before = _processor.Pipeline.Buffer.DensityCpu.ToArray();

    _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
    VolumeObject second = _registry.objects[1];
    second.transform.localPosition = new Vector3(0f, 7f, 0f);

    LogAssert.Expect(LogType.Warning, new Regex("Objects exceed grid bounds"));
    _processor.RebuildModel();

    CollectionAssert.AreEqual(before, _processor.Pipeline.Buffer.DensityCpu.ToArray());
}
```

Falls `NativeArray<T>.ToArray()` im verwendeten Unity-Paket nicht verfügbar ist, eine lokale Copy-Schleife verwenden. Wichtig ist der Vergleich des vollständigen Buffers vor und nach dem abgelehnten Rebuild.

**Step 2: Test ausführen und Fehler bestätigen**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter VolumeProcessorAddObjectTests.RebuildModel_WhenBoundsDoNotFitAndAutoExpandIsOff_PreservesPreviousBuffer \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-out-of-bounds-red.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-out-of-bounds-red.log"
```

Expected: FAIL — der aktuelle `CheckBoundsFit()`-Pfad gibt bei `autoExpand == false` `true` zurück; der Full-Rebuild überschreibt den Buffer mit positiven/leerem SDF.

**Step 3: Auto-Expand-Test schreiben**

```csharp
[Test]
public void RebuildModel_WhenSecondObjectIsOutsideAndAutoExpandIsOn_ResizesAndBuildsBothShapes()
{
    _processor.autoExpand = true;
    _processor.Initialize();

    _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
    VolumeObject first = _registry.objects[0];
    first.transform.localPosition = Vector3.zero;

    _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);
    VolumeObject second = _registry.objects[1];
    second.transform.localPosition = new Vector3(0f, 7f, 0f);
    _processor.RebuildModel();

    Bounds layoutBounds = GetLayoutBounds(_processor.Pipeline.Buffer.Layout);
    Assert.That(layoutBounds.Contains(first.transform.position), Is.True);
    Assert.That(layoutBounds.Contains(second.transform.position), Is.True);
    AssertBufferSampleInside(_processor.Pipeline.Buffer, first.transform.position);
    AssertBufferSampleInside(_processor.Pipeline.Buffer, second.transform.position);
}
```

Dieser Test muss vor dem Fix voraussichtlich scheitern, weil `ResizeGrid()` den neuen Pipeline-Zustand nur dirty markiert und `RebuildModel()` danach früh zurückkehrt. Im Batch/EditMode-Test gibt es keinen garantierten späteren Editor-Tick, der den neuen Buffer synchron füllt.

**Step 4: Commit**

```bash
git add Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs
git commit -m "test: reproduce geometry loss outside volume bounds"
```

---

### Task 3: Bounds-Prüfung nicht-destruktiv machen

**Objective:** Bei deaktiviertem Auto Expand einen ungültigen Full-Rebuild vor jeder Buffer-/Mesh-Änderung stoppen.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs:223-352`
- Test: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`

**Step 1: Rückgabesemantik von `CheckBoundsFit` präzisieren**

Die Methode soll nur dann `true` zurückgeben, wenn der aktive Pipeline-Buffer die Gesamt-Bounds tatsächlich abdeckt und ein Rebuild sicher ist.

Minimaler Fix im deaktivierten Auto-Expand-Pfad:

```csharp
if (!autoExpand)
{
    Debug.LogWarning(
        $"[VolumeProcessor] Rebuild skipped because objects exceed grid bounds. " +
        $"Grid: {gridMin:F1}..{gridMax:F1}, Objects: {total.min:F1}..{total.max:F1}. " +
        "Enable Auto Expand or increase Bounds Extent to include all objects.");
    return false;
}
```

Die Kommentare und Caller müssen angepasst werden: `false` bedeutet „Rebuild jetzt nicht ausführen“, nicht ausschließlich „Resize wurde gestartet“.

**Step 2: Alle drei Caller auf frühes, nicht-destruktives Return prüfen**

Betroffene Stellen:

- `VolumeProcessor.RebuildPipeline()`
- `VolumeProcessor.RebuildModel()`
- `VolumeProcessor.RebuildDirty()`

Alle müssen `CheckBoundsFit()` **vor** `_pipeline.Rebuild(...)`, `Scheduler.ClearPending()`, `DirtyChunks.MarkAllDirty()` und jeder Renderer-Leerung aufrufen. Beim `false`-Resultat dürfen Buffer und bestehende Chunk-Renderer unangetastet bleiben.

**Step 3: Negativen Regressionstest erneut ausführen**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter VolumeProcessorAddObjectTests.RebuildModel_WhenBoundsDoNotFitAndAutoExpandIsOff_PreservesPreviousBuffer \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-out-of-bounds-green.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-out-of-bounds-green.log"
```

Expected: PASS; Log enthält genau die neue Warnung und der Buffer ist byte-/wertgleich zum Stand vor dem abgelehnten Rebuild.

**Step 4: Commit**

```bash
git add Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs
git commit -m "fix: preserve volume geometry when bounds do not fit"
```

---

### Task 4: Auto-Resize im selben Rebuild abschließen

**Objective:** Mit aktiviertem Auto Expand das neue Layout sofort samplen und meshen, statt auf einen möglicherweise nicht laufenden Editor-Tick zu warten.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs:261-352`
- Test: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`

**Step 1: `ResizeGrid` auf eine klare Postcondition umstellen**

Nach `ResizeGrid(total)` muss `_pipeline` vollständig initialisiert sein und ein Layout besitzen, das `total` enthält. `ResizeGrid()` soll nicht selbst einen späteren Rebuild über `_pipeline.MarkDirty()` voraussetzen.

Entfernen bzw. ersetzen:

```csharp
_pipeline.MarkDirty();
```

Der Caller soll nach erfolgreichem Resize in demselben Call-Stack normal mit `_pipeline.Rebuild(composer, isoLevel)` fortfahren.

**Step 2: `CheckBoundsFit` nach Resize erfolgreich zurückkehren lassen**

```csharp
ResizeGrid(total);
return true;
```

Vor dem Return optional mit einer kleinen privaten Hilfsmethode erneut prüfen, dass `Pipeline.Buffer.Layout` die erforderlichen Bounds enthält. Bei einem unerwarteten Fehlschlag warnen und `false` liefern; keine rekursive Resize-/Rebuild-Schleife einführen.

**Step 3: Verwaiste Ausgabeobjekte verhindern**

`ResizeGrid()` erzeugt aktuell kurzzeitig `PipelineMeshOutput` und zerstört es wieder. Vor und nach dem Resize prüfen:

- Es existiert genau ein `VisualOutput`.
- Es existiert genau ein `Chunks`-Parent.
- `_chunksParent` zeigt auf diesen Parent.
- Alle alten Chunk-Kinder werden entsorgt, bevor neue Chunk-Renderer erzeugt werden.
- Der neue `ChunkRenderManager` ist mit der neuen `ChunkGridSize` initialisiert.

Wenn `EnsureVisualOutput()` oder ein vorhandener `Chunks`-Parent wiederverwendet werden kann, diesen Weg verwenden, statt weitere gleichnamige Objekte anzulegen. Keine allgemeine Hierarchie-Refaktorierung in diesen Bugfix aufnehmen.

**Step 4: Auto-Expand-Test ausführen**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter VolumeProcessorAddObjectTests.RebuildModel_WhenSecondObjectIsOutsideAndAutoExpandIsOn_ResizesAndBuildsBothShapes \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-auto-expand-green.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-auto-expand-green.log"
```

Expected: PASS; Layout enthält beide Objektzentren, beide Buffer-Samples liegen innerhalb der Oberfläche, keine Exception und keine leere Pipeline nach dem Resize.

**Step 5: Commit**

```bash
git add Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs
git commit -m "fix: rebuild resized volume pipeline synchronously"
```

---

### Task 5: Doppelten Rebuild beim Hinzufügen entfernen

**Objective:** `AddObject()` soll genau einen Full-Rebuild auslösen und trotzdem einen Undo-Eintrag erzeugen.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs:379-399`
- Modify: `Assets/Scripts/VolumeSystem/Core/Undo/CommandStack.cs:28-51` nur falls eine explizite „already executed“-API notwendig ist
- Modify: `Assets/Scripts/VolumeSystem/Core/Undo/CompositionCommand.cs:3-35` nur falls Command-Ausführung und State-Notification sauber getrennt werden müssen
- Modify: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`

**Step 1: Testbare Rebuild-Zählung hinzufügen, ohne Produktions-Debug-API aufzublähen**

Bevor Produktionscode verändert wird, im Test den vorhandenen `BuildVersion`-Wert nutzen:

```csharp
[Test]
public void AddObject_PerformsExactlyOneRebuild()
{
    _processor.Initialize();
    int before = _processor.BuildVersion;

    _processor.AddObject(VolumeShapeType.Sphere, VolumeOperationRole.Add);

    Assert.That(_processor.BuildVersion, Is.EqualTo(before + 1));
}
```

Falls `RebuildModel()` den Build-Zähler aktuell nicht erhöht, den Test stattdessen über eine interne Rebuild-Notification absichern. Keine globale Log-Auswertung als Unit-Test verwenden.

**Step 2: Test ausführen und doppelten Rebuild bestätigen**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter VolumeProcessorAddObjectTests.AddObject_PerformsExactlyOneRebuild \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-single-rebuild-red.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-single-rebuild-red.log"
```

Expected: FAIL — aktuell kommt ein Rebuild über `CommandStack.OnStateChanged` und ein zweiter explizit aus `AddObject()`.

**Step 3: Minimalen Single-Rebuild-Pfad implementieren**

Bevorzugte minimale Variante:

- `CommandStack.Push(new AddObjectCommand(...))` bleibt zuständig für State-Notification und Rebuild.
- Den nachfolgenden expliziten `RebuildModel()`-Aufruf aus `AddObject()` entfernen.
- Den Debug-Log vor oder nach `Push` so platzieren, dass er den korrekten `total`-Wert ausgibt, ohne einen zweiten Rebuild anzustoßen.

Nur falls dadurch Undo/Redo-Semantik inkonsistent wird, eine explizite `PushAlreadyExecuted(ICommand)`-Methode einführen, die den Command speichert und genau einmal `OnStateChanged` feuert. Nicht gleichzeitig `CommandStack.Push` semantisch für alle bestehenden Commands ändern.

**Step 4: Single-Rebuild- und Zwei-Objekt-Tests ausführen**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter VolumeProcessorAddObjectTests \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-add-object-green.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-add-object-green.log"
```

Expected: Alle Tests in `VolumeProcessorAddObjectTests` PASS; pro `AddObject` erscheint genau ein `RebuildModel called` im fokussierten Log.

**Step 5: Commit**

```bash
git add Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs Assets/Scripts/VolumeSystem/Core/Undo/CommandStack.cs Assets/Scripts/VolumeSystem/Core/Undo/CompositionCommand.cs Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs
git commit -m "fix: rebuild once when adding a volume object"
```

Nur tatsächlich geänderte Dateien stagen.

---

### Task 6: Sicheren Standard und Inspector-Hinweis setzen

**Objective:** Neue Volume-Prozessoren sollen den erwarteten „Objekt hinzufügen und sofort sehen“-Pfad verwenden, während ein bewusst deaktiviertes Auto Expand weiterhin respektiert wird.

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs:20-24`
- Modify: `Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs:40-90`
- Modify: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`

**Step 1: Default-Test schreiben**

```csharp
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
```

**Step 2: Test ausführen und Failure bestätigen**

Expected: FAIL — aktueller Default ist `false`.

**Step 3: Minimalen Default ändern**

```csharp
[SerializeField] public bool autoExpand = true;
```

Keine automatische Überschreibung explizit serialisierter `false`-Werte implementieren.

**Step 4: Inspector-Warnung ergänzen**

Wenn `autoExpand == false`, unter den Bounds-Einstellungen eine `HelpBox` anzeigen:

```csharp
EditorGUILayout.HelpBox(
    "Objects outside Bounds Extent are not rebuilt. Existing geometry is preserved until the bounds are enlarged or Auto Expand is enabled.",
    MessageType.Info);
```

Die Meldung soll sichtbar machen, warum ein außerhalb liegendes zweites Objekt nicht erscheint, ohne vorhandene Geometrie zu löschen.

**Step 5: Tests ausführen**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter VolumeProcessorAddObjectTests \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-defaults-green.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-defaults-green.log"
```

Expected: PASS.

**Step 6: Commit**

```bash
git add Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs
git commit -m "fix: enable safe volume bounds expansion by default"
```

---

### Task 7: Bestehende Kompositions- und Burst-Semantik regressionsprüfen

**Objective:** Sicherstellen, dass der Fix nicht nur zwei Add-Sphären unterstützt, sondern vorhandene Operationen und Burst-Sampling unverändert lässt.

**Files:**
- Test only: `Assets/Tests/Editor/SceneCompositeSDFTests.cs`
- Test only: `Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs`
- Test only: `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`

**Step 1: Fokussierte bestehende Tests ausführen**

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter "SceneCompositeSDFTests;BurstSdfSceneSnapshotTests;VolumeProcessorAddObjectTests" \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-composition-regression.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/volume-composition-regression.log"
```

Falls Unitys `-testFilter` in dieser Version keine Semikolon-Liste akzeptiert, die drei Fixtures separat ausführen.

Expected:

- Zwei Add-Objekte werden per Union kombiniert.
- Subtract und Intersect bleiben unabhängig von der Objektlisten-Reihenfolge gruppiert.
- Managed- und Burst-Snapshot liefern dieselben Werte.
- Keine `InvalidOperationException`, `ObjectDisposedException`, Native-Collection-Leaks oder `0 verts` für Chunks, die eine getestete Oberfläche schneiden.

**Step 2: Compile-Log prüfen**

Prüfen auf:

```text
error CS
Compilation failed
NullReferenceException
InvalidOperationException
ObjectDisposedException
A Native Collection has not been disposed
```

Expected: Keine Treffer aus dem Testlauf.

**Step 3: Commit nur falls Testanpassungen nötig waren**

```bash
git add Assets/Tests/Editor/SceneCompositeSDFTests.cs Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs
git commit -m "test: verify volume composition after bounds fix"
```

---

### Task 8: Manuellen Unity-End-to-End-Fall verifizieren

**Objective:** Den vom Benutzer beschriebenen Inspector-Ablauf in einer echten Scene reproduzieren und sichtbar bestätigen.

**Files:**
- No project file changes required
- Output only: `Temp/volume-second-object-manual-check.log`

**Step 1: Saubere Test-Scene verwenden**

- Neues leeres GameObject erstellen.
- `VolumeProcessor` hinzufügen.
- Prüfen, dass `VolumeObjectRegistry` automatisch vorhanden ist.
- Auflösung temporär auf `32 × 32 × 32`, Chunk Size auf `16` setzen.
- Auto Expand aktivieren.

**Step 2: Erstes Objekt hinzufügen**

- Shape: Sphere
- Role: Add
- „Add Object“ klicken.
- Position auf `(0, 0, 0)` setzen.
- Prüfen: Registry Count `1`, sichtbare Geometrie vorhanden.

**Step 3: Zweites Objekt außerhalb des ersten Grid-Bounds hinzufügen**

- Zweite Sphere/Add hinzufügen.
- Position auf `(0, 7, 0)` setzen.
- Rebuild auslösen.
- Prüfen:
  - Registry Count `2`
  - Grid wurde erweitert
  - beide Sphären sichtbar
  - keine vorübergehend dauerhaft leere Chunk-Hierarchie
  - genau ein Full-Rebuild pro Add-Aktion

**Step 4: Verhalten mit deaktiviertem Auto Expand prüfen**

- Zuletzt gültige Geometrie anzeigen lassen.
- Auto Expand deaktivieren.
- Ein weiteres Objekt außerhalb der Bounds verschieben/hinzufügen.
- Rebuild auslösen.
- Prüfen:
  - Warnung erklärt den Bounds-Konflikt
  - bisherige Geometrie bleibt sichtbar
  - das neue außerhalb liegende Objekt wird erst nach größerem Bounds Extent oder erneutem Auto Expand dargestellt

**Step 5: Vollständige EditMode-Suite ausführen**

Unity muss geschlossen sein, bevor derselbe Projektpfad im Batchmodus geöffnet wird.

Run:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/editmode-results.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/editmode-tests.log"
```

Expected: Unity Exit-Code `0`, keine Compilerfehler, keine fehlgeschlagenen EditMode-Tests.

**Step 6: Arbeitsbaum und Patch prüfen**

Run:

```bash
git diff --check
git status --short
git diff -- Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs \
  Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs \
  Assets/Scripts/VolumeSystem/Core/Undo/CommandStack.cs \
  Assets/Scripts/VolumeSystem/Core/Undo/CompositionCommand.cs \
  Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs
```

Expected: Kein Whitespace-Fehler; keine versehentlichen Änderungen an Scenes, Prefabs, `.meta`-GUIDs oder nicht beteiligten Assets.

**Step 7: Finaler Commit**

Nur falls nach dem manuellen Lauf noch notwendige Änderungen entstanden sind:

```bash
git add <nur-tatsächlich-geänderte-Dateien>
git commit -m "fix: keep geometry when adding volume objects"
```

---

## Wahrscheinlich zu ändernde Dateien

- `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs`
  - Bounds-Check abbrechen statt leeren Rebuild ausführen
  - Resize synchron abschließen
  - doppelten Add-Rebuild entfernen
  - sicherer Auto-Expand-Default
- `Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs`
  - verständlicher Hinweis bei deaktiviertem Auto Expand
- `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs`
  - neue fokussierte Regressionstests
- `Assets/Tests/Editor/VolumeProcessorAddObjectTests.cs.meta`
  - neue Unity-Metadatei mit neuer GUID
- Eventuell, nur wenn für saubere Command-Semantik erforderlich:
  - `Assets/Scripts/VolumeSystem/Core/Undo/CommandStack.cs`
  - `Assets/Scripts/VolumeSystem/Core/Undo/CompositionCommand.cs`

Nicht geplant:

- Änderung der bestehenden Script-GUIDs
- Scene-/Prefab-Migration
- Refaktorierung des gesamten Renderers oder Schedulers
- Wiederbelebung der mit `DISABLED_OLD_API` deaktivierten Legacy-Tests
- Änderung der Union/Subtract/Intersect-Semantik

---

## Risiken und Trade-offs

1. **Bestehende serialisierte `autoExpand: false`-Werte:** Die Änderung des C#-Defaults wirkt nicht zwingend auf bereits gespeicherte Komponenten. Deshalb muss der nicht-destruktive False-Pfad unabhängig vom Default korrekt sein. Keine stille Migration eines bewusst gespeicherten Wertes durchführen.
2. **Resize-Kosten:** Ein synchroner Resize plus Full-Rebuild kann bei `128³` oder größer spürbar dauern. Für Korrektheit und EditMode-Determinismus ist das akzeptabel; Optimierung oder asynchrones Progress-UI gehört nicht in diesen Fix.
3. **Mesh-gegen-Buffer-Test:** Buffer-Werte beweisen die Komposition, aber nicht allein die sichtbaren Meshes. Deshalb ist zusätzlich die manuelle Chunk-Renderer-Verifikation erforderlich.
4. **Doppelte Rebuild-Entfernung und Undo:** `AddObjectCommand.Execute()` ist aktuell ein No-op, weil das GameObject vor `Push` erstellt wird. Der Fix darf Redo nicht weiter verschlechtern; falls Redo schon defekt ist, nur die minimale, testbare Single-Rebuild-Korrektur vornehmen und einen separaten Follow-up dokumentieren.
5. **Geöffnete Unity-Instanz:** Unity-Batchtests können denselben Projektpfad nicht parallel zur geöffneten Editorinstanz verwenden. Vor den finalen Batchläufen den Editor kontrolliert schließen; keine laufende Instanz gewaltsam beenden.
6. **Lokale uncommittete Änderungen:** Der Branch enthält bereits Rename-/Merge-Fixes. Keine fremden Änderungen zurücksetzen oder pauschal stagen.
7. **`0 chunks` im Log:** `VolumePipeline.Rebuild` loggt die Queue-Größe vor `MarkAllDirty`, wodurch `0 chunks` allein missverständlich ist. Der tatsächliche Fehler wird durch die nachfolgenden `0 verts, 0 indices` und den Bounds-Warnpfad bestätigt. Logging-Aufräumarbeiten nur durchführen, wenn sie für den Regressionstest nötig sind.

## Offene Frage für die Implementierung

Wenn der Benutzer bei bereits serialisierten Komponenten `autoExpand == false` bewusst beibehalten will, ist das geplante Verhalten: **bestehende Geometrie erhalten und das neue außerhalb liegende Objekt erst nach Bounds-Korrektur anzeigen**. Soll stattdessen jedes `AddObject()` unabhängig von dieser Einstellung automatisch expandieren, wäre das eine andere Produktentscheidung und sollte vor Task 6 ausdrücklich bestätigt werden. Der Kernfix in Tasks 1–5 bleibt in beiden Varianten gültig.

---

## Abschluss-Checkliste

- [ ] Zwei Add-Objekte bleiben gleichzeitig in Registry und Snapshot.
- [ ] Beide Objektzentren liefern negative/innere SDF-Werte im Pipeline-Buffer.
- [ ] Out-of-Bounds + Auto Expand an: Grid wächst und beide Objekte erscheinen im selben Rebuild.
- [ ] Out-of-Bounds + Auto Expand aus: vorherige Geometrie bleibt erhalten; klare Warnung.
- [ ] `AddObject()` löst genau einen Rebuild aus.
- [ ] Keine doppelten `VisualOutput`-/`Chunks`-Hierarchien nach Resize.
- [ ] Managed- und Burst-Kompositionstests bestehen.
- [ ] Vollständige EditMode-Suite besteht.
- [ ] Keine Script-GUIDs oder Scene-/Prefab-Referenzen verändert.
- [ ] `git diff --check` besteht.
