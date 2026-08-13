# Unity-Kompilierungsfehler beheben Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Alle aktuellen Unity-C#-Kompilierungsfehler im Projekt beseitigen, den offenen Rename/Merge-Konflikt sauber auflösen und anschließend Editor-Tests sowie einen vollständigen Unity-Batchlauf erfolgreich abschließen.

**Architecture:** Die laufende Umbenennung `VolumeModel` → `VolumeProcessor` und `VolumeSceneComposer` → `VolumeObjectRegistry` wird als echte Unity-Asset-Umbenennung abgeschlossen. Dabei werden die bestehenden `.meta`-GUIDs erhalten, damit Szenen und Prefabs ihre Komponenten nicht verlieren; die veraltete `VolumeSceneComposer.cs` wird nicht parallel zur neuen Registry weitergeführt. Danach werden neu sichtbar werdende Compilerfehler iterativ anhand des Unity-Logs korrigiert, ohne nicht zusammenhängende Änderungen anzufassen.

**Tech Stack:** Unity 6000.4.1f1, C# 9, Unity Test Framework/NUnit, Git, Unity YAML-Assets und `.meta`-GUIDs.

---

## Aktueller Kontext und Annahmen

- Workspace: `C:\Users\tgent\workspaces\sdf-geometry-generation`
- Unity läuft bereits mit diesem Projekt; deshalb kann keine zweite Batch-Instanz gleichzeitig geöffnet werden.
- Die früheren `CS8300`-Fehler in `VolumeObject.cs` sind behoben; es gibt dort keine Konfliktmarker mehr.
- Der aktuelle `Editor.log` meldet als jüngste primäre Fehler sechs `CS0246`-Fundstellen in `Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs:96,104,118`, weil `VolumeModel` gelöscht/umbenannt wurde.
- `VolumeSceneComposer.cs` ist im Git-Index weiterhin ein ungelöster Modify/Delete-Konflikt (`UD`), obwohl die neue `VolumeObjectRegistry.cs` bereits existiert.
- `CONTEXT.md` bestätigt ausdrücklich die Renames:
  - `VolumeModel` → `VolumeProcessor`
  - `VolumeSceneComposer` → `VolumeObjectRegistry`
- Die alten Unity-GUIDs sind bereits in Szenen referenziert:
  - Composer: `1db7fca8924706740a533d8d955ba297`
  - Model: `390c39fbad2c5a4468a59eeef512bf0d`
- Die neuen Dateien besitzen derzeit andere GUIDs:
  - Registry: `5f699e9ec67679e4c9670e6e3e7eb64b`
  - Processor: `a920c32420aeec848b7fc37027ea396e`
- Annahme: Die neuen Klassennamen sollen die alten vollständig ersetzen; es wird keine Legacy-Kompatibilitätsklasse `VolumeSceneComposer` benötigt.
- Bereits vorhandene, nicht zusammenhängende Änderungen und die große Änderung an `Assets/Scenes/scene_outdoor_1.unity` dürfen nicht pauschal zurückgesetzt werden.

## Vorgeschlagener Ansatz

1. Zuerst einen reproduzierbaren roten Baseline-Check für Compilerfehler, Konfliktstatus und Unity-GUID-Integrität etablieren.
2. Den Rename-Konflikt auf Dateisystem-, Git- und Unity-Asset-Ebene konsistent abschließen.
3. Die alten `.meta`-GUIDs auf den umbenannten Skripten erhalten oder alle betroffenen YAML-Referenzen kontrolliert migrieren; bevorzugt wird GUID-Erhalt, weil dies dem Unity-Rename-Verhalten entspricht und Szenenänderungen minimiert.
4. Unity neu kompilieren lassen und jeweils nur den ersten echten Folgefehler beheben.
5. Editor-Tests und einen finalen Batchlauf in einer einzigen Unity-Instanz ausführen.

---

### Task 1: Reproduzierbaren Fehler-Baseline-Check erstellen

**Objective:** Den aktuellen Fehlerzustand und die Rename-Invarianten mit einem schnellen, deterministischen Check festhalten.

**Files:**
- Create: `Tools/verify_volume_rename.py`
- Read: `C:\Users\tgent\AppData\Local\Unity\Editor\Editor.log`
- Read: `Assets/Scripts/VolumeSystem/Composition/*.cs`
- Read: `Assets/Scripts/VolumeSystem/Composition/*.cs.meta`
- Read: `Assets/**/*.unity`
- Read: `Assets/**/*.prefab`

**Step 1: Failing verification script schreiben**

Das Skript soll mindestens prüfen:

```python
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
COMPOSITION = ROOT / "Assets/Scripts/VolumeSystem/Composition"

assert not (COMPOSITION / "VolumeSceneComposer.cs").exists()
assert not (COMPOSITION / "VolumeModel.cs").exists()
assert (COMPOSITION / "VolumeObjectRegistry.cs").exists()
assert (COMPOSITION / "VolumeProcessor.cs").exists()

registry_meta = (COMPOSITION / "VolumeObjectRegistry.cs.meta").read_text()
processor_meta = (COMPOSITION / "VolumeProcessor.cs.meta").read_text()
assert "guid: 1db7fca8924706740a533d8d955ba297" in registry_meta
assert "guid: 390c39fbad2c5a4468a59eeef512bf0d" in processor_meta

for path in (ROOT / "Assets").rglob("*"):
    if path.suffix.lower() not in {".cs", ".unity", ".prefab", ".asset", ".meta"}:
        continue
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    assert not re.search(r"(?m)^(<<<<<<<|=======|>>>>>>>)", text), path

unmerged = subprocess.run(
    ["git", "ls-files", "-u"], cwd=ROOT, text=True, capture_output=True, check=True
).stdout
assert not unmerged.strip(), unmerged
```

Zusätzlich soll das Skript für jede in Szenen/Prefabs verwendete Script-GUID prüfen, dass genau eine passende `.meta`-Datei existiert. `_Recovery` darf separat gemeldet werden, aber keine produktive Szene oder kein Prefab darf auf eine fehlende Script-GUID zeigen.

**Step 2: Baseline ausführen und erwartetes Scheitern bestätigen**

Run:

```bash
python Tools/verify_volume_rename.py
```

Expected: FAIL, weil `VolumeSceneComposer.cs` noch existiert, der Index einen `UD`-Konflikt enthält und Registry/Processor noch nicht die alten GUIDs besitzen.

**Step 3: Aktuelle Unity-Fehler separat erfassen**

Run:

```bash
python -c "from pathlib import Path; import re; p=Path(r'C:\Users\tgent\AppData\Local\Unity\Editor\Editor.log'); lines=p.read_text(encoding='utf-8',errors='replace').splitlines(); print('\n'.join(x for x in lines if re.search(r'error CS[0-9]+', x)))"
```

Expected: Die jüngsten Fehler zeigen ausschließlich `CS0246` für `VolumeModel` in `VolumeSceneComposer.cs:96,104,118`; ältere `CS8300`-Zeilen dürfen als Log-Historie erkannt und nicht als aktueller Zustand fehlinterpretiert werden.

**Step 4: Commit**

```bash
git add Tools/verify_volume_rename.py
git commit -m "test: verify volume component rename integrity"
```

> Falls der bestehende Merge/Rebase keine Zwischen-Commits erlaubt, diesen Commit-Schritt bis nach Konfliktauflösung verschieben.

---

### Task 2: `VolumeSceneComposer`-Konflikt als Rename zu `VolumeObjectRegistry` auflösen

**Objective:** Die veraltete Klasse entfernen und nur die neue Registry als authoritative Implementierung behalten.

**Files:**
- Delete: `Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs`
- Delete old path: `Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs.meta`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs.meta`

**Step 1: Inhaltliche Parität vor dem Löschen prüfen**

Vergleichen:

```bash
git diff --no-index -- Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs
```

Expected: Die Registry enthält die neue `VolumeProcessor`-Integration, zero-allocation Bounds-Transformation und `GetTotalBounds()`; es gibt keine nur im alten Composer vorhandene Funktion, die weiterhin benötigt wird.

**Step 2: Alte Composer-GUID auf die Registry übertragen**

`Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs.meta` soll exakt enthalten:

```yaml
fileFormatVersion: 2
guid: 1db7fca8924706740a533d8d955ba297
```

Dadurch bleiben bestehende Unity-Komponenten trotz Klassen-/Datei-Rename verbunden.

**Step 3: Veraltete Composer-Dateien entfernen**

- `VolumeSceneComposer.cs` entfernen.
- Den untracked/alten `VolumeSceneComposer.cs.meta` entfernen.
- Sicherstellen, dass kein paralleler `VolumeSceneComposer`-Typ mehr kompiliert wird.

**Step 4: Git-Konflikt explizit auflösen**

```bash
git add -A -- Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs.meta Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs.meta
git ls-files -u -- Assets/Scripts/VolumeSystem/Composition
```

Expected: Keine Ausgabe von `git ls-files -u` für diese Dateien; Git darf den Vorgang als Rename oder Delete+Add darstellen.

**Step 5: Baseline-Check ausführen**

```bash
python Tools/verify_volume_rename.py
```

Expected: Der Composer-bezogene Teil besteht; der Check darf noch wegen der Processor-GUID oder weiterer ungelöster Dateien scheitern.

**Step 6: Commit**

```bash
git add Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs Assets/Scripts/VolumeSystem/Composition/VolumeSceneComposer.cs.meta Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs Assets/Scripts/VolumeSystem/Composition/VolumeObjectRegistry.cs.meta
git commit -m "refactor: complete volume object registry rename"
```

---

### Task 3: `VolumeModel`-GUID beim Rename zu `VolumeProcessor` erhalten

**Objective:** Bestehende Szenen und Prefabs ohne Missing-Script-Komponenten auf `VolumeProcessor` migrieren.

**Files:**
- Delete old path: `Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs`
- Delete old path: `Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs.meta`
- Modify: `Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs.meta`
- Inspect only unless migration is required: `Assets/**/*.unity`
- Inspect only unless migration is required: `Assets/**/*.prefab`

**Step 1: Alten Model-GUID auf Processor übertragen**

`Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs.meta` soll exakt enthalten:

```yaml
fileFormatVersion: 2
guid: 390c39fbad2c5a4468a59eeef512bf0d
```

**Step 2: Doppelte GUIDs ausschließen**

Run:

```bash
python Tools/verify_volume_rename.py
```

Expected: Jede `.meta`-GUID kommt genau einmal vor; produktive Szenen/Prefabs mit dem alten Model-GUID lösen jetzt auf `VolumeProcessor.cs` auf.

**Step 3: Szenen-Migration nur bei nachgewiesener Notwendigkeit durchführen**

Falls `scene_outdoor_1.unity` bereits die temporären neuen GUIDs `5f699...` oder `a920...` enthält, nur diese Script-GUID-Felder gezielt auf die erhaltenen alten GUIDs zurückstellen. Keine generierte Chunk-Geometrie, Beleuchtung oder andere Szeneninhalte in diesem Task verändern.

**Step 4: Commit**

```bash
git add Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs Assets/Scripts/VolumeSystem/Composition/VolumeModel.cs.meta Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs Assets/Scripts/VolumeSystem/Composition/VolumeProcessor.cs.meta Assets/Scenes/scene_outdoor_1.unity
git commit -m "refactor: preserve processor references across rename"
```

Nur `scene_outdoor_1.unity` stagen, wenn dort tatsächlich eine gezielte GUID-Migration vorgenommen wurde.

---

### Task 4: Veraltete Typreferenzen im aktiven Code bereinigen

**Objective:** Sicherstellen, dass kompilierter Code ausschließlich die neuen Typnamen verwendet.

**Files:**
- Modify as indicated by search: `Assets/Scripts/VolumeSystem/**/*.cs`
- Likely unchanged/legacy guarded: `Assets/Scripts/VolumeSystem/Composition/VolumeModelOld.cs`
- Inspect: `Assets/Scripts/VolumeSystem/Editor/VolumeObjectEditor.cs`
- Inspect: `Assets/Scripts/VolumeSystem/Editor/VolumeObjectRegistryEditor.cs`
- Inspect: `Assets/Scripts/VolumeSystem/Editor/VolumeProcessorEditor.cs`

**Step 1: Aktive Alt-Typreferenzen suchen**

Suche nach:

```text
\bVolumeModel\b
\bVolumeSceneComposer\b
```

Jeden Treffer klassifizieren:

- aktiver C#-Code → auf `VolumeProcessor` bzw. `VolumeObjectRegistry` migrieren;
- `#if LEGACY`/`#if DISABLED_OLD_API` → nur ändern, wenn das Symbol im aktuellen Build aktiv sein kann;
- Dokumentation oder archivierte Recovery-Szene → nicht als Compilerfehler behandeln.

**Step 2: Failing compile/reference check bestätigen**

Run:

```bash
python Tools/verify_volume_rename.py
```

Expected before edits: FAIL mit den verbleibenden aktiven Alt-Typreferenzen.

**Step 3: Minimale Referenzänderungen durchführen**

Beispiel:

```csharp
VolumeProcessor processor = GetComponent<VolumeProcessor>();
VolumeObjectRegistry registry = GetComponent<VolumeObjectRegistry>();
```

Keine Alias-/Shim-Klassen hinzufügen, solange keine externe API-Kompatibilitätsanforderung nachgewiesen ist.

**Step 4: Check erneut ausführen**

```bash
python Tools/verify_volume_rename.py
```

Expected: PASS für Konfliktmarker, unmerged Index, GUID-Auflösung und aktive Alt-Typreferenzen.

**Step 5: Commit**

```bash
git add Assets/Scripts/VolumeSystem Tools/verify_volume_rename.py
git commit -m "fix: remove stale volume type references"
```

---

### Task 5: Unity-Kompilierung aus der bestehenden Editor-Instanz verifizieren

**Objective:** Die tatsächliche Unity-Kompilierung grün bekommen und Folgefehler einzeln beheben.

**Files:**
- Read: `C:\Users\tgent\AppData\Local\Unity\Editor\Editor.log`
- Modify: nur die Datei, die vom jeweils ersten aktuellen Compilerfehler genannt wird

**Step 1: Vor dem Refresh Log-Offset erfassen**

Die aktuelle Zeilenanzahl von `Editor.log` speichern, damit alte `CS8300`/`CS0246`-Einträge nicht erneut als aktuelle Fehler gezählt werden.

**Step 2: Unity-Refresh auslösen**

Da das Projekt bereits geöffnet ist, bevorzugt die laufende Editor-Instanz verwenden:

- Änderungen speichern und Unity den automatischen Asset Refresh durchführen lassen; oder
- im Editor `Assets > Refresh` auslösen.

Keine zweite Unity-Instanz öffnen.

**Step 3: Nur neue Logzeilen prüfen**

Expected:

- kein `error CS...` nach dem gespeicherten Offset;
- `Assembly-CSharp.dll` und `Assembly-CSharp-Editor.dll` werden erfolgreich erzeugt;
- die nachgelagerte Burst-Meldung `Failed to resolve assembly: Assembly-CSharp-Editor` verschwindet, sobald die C#-Kompilierung erfolgreich ist.

**Step 4: Falls ein Folgefehler erscheint, tight loop anwenden**

Für jeden neuen Fehler:

1. exakte Datei/Zeile lesen;
2. Ursache gegen Rename-/API-Änderungen prüfen;
3. nur diesen Fehler minimal beheben;
4. Unity erneut refreshen;
5. neue Logzeilen prüfen.

Nicht gleichzeitig Warnungen wie `MovementComponent.name` oder Package-Warnungen ändern, solange sie den Build nicht blockieren.

**Step 5: Commit**

```bash
git add <nur-die-tatsächlich-geänderten-Dateien>
git commit -m "fix: resolve unity compilation errors"
```

---

### Task 6: Unity Editor Tests ausführen

**Objective:** Nach erfolgreicher Kompilierung Regressionen in Pipeline, Snapshot, Meshing und Renderer ausschließen.

**Files:**
- Test: `Assets/Tests/Editor/BurstSdfSceneSnapshotTests.cs`
- Test: `Assets/Tests/Editor/FlatOctreeBurstFrontierTests.cs`
- Test: `Assets/Tests/Editor/PipelineTests.cs`
- Test: `Assets/Tests/Editor/SceneCompositeSDFTests.cs`
- Test: `Assets/Tests/Editor/VolumeMeshRendererClusterTests.cs`
- Test: `Assets/Tests/Editor/VolumePipelineTests.cs`
- Test result: `Temp/hermes-editmode-results.xml`
- Test log: `Temp/hermes-editmode.log`

**Step 1: Laufende Unity-Instanz kontrolliert schließen**

Vor dem Batchlauf das Projekt im Editor speichern und Unity regulär schließen. Nicht per `kill` beenden, um Szenen-/Assetverlust zu vermeiden.

**Step 2: EditMode-Suite starten**

Run:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/hermes-editmode-results.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/hermes-editmode.log"
```

Expected: Exit-Code `0`, keine Compilerfehler, alle aktivierten EditMode-Tests bestanden.

**Step 3: Fehlgeschlagene Tests einzeln reproduzieren**

Für jeden echten Testfehler:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -runTests -testPlatform EditMode \
  -testFilter "Fully.Qualified.Test.Name" \
  -testResults "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/hermes-single-test.xml" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/hermes-single-test.log"
```

Expected before fix: reproduzierbarer FAIL. Danach minimal korrigieren und denselben Test bis PASS wiederholen.

**Step 4: Gesamtsuite erneut ausführen**

Expected: Exit-Code `0`; Test-XML enthält keine Failures oder Errors.

**Step 5: Commit**

```bash
git add <regression-fix-files>
git commit -m "test: restore passing unity editor suite"
```

Nur falls Test-Reparaturen erforderlich waren.

---

### Task 7: Finalen Compile-, Asset- und Git-Check durchführen

**Objective:** Beweisen, dass der Fix vollständig ist und keine halb aufgelösten Merge-/Unity-Assets hinterlässt.

**Files:**
- Read: `Temp/hermes-final-compile.log`
- Read: `Temp/hermes-editmode-results.xml`
- Read: Git index/worktree

**Step 1: Finalen reinen Unity-Compile ausführen**

Run:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Users/tgent/workspaces/sdf-geometry-generation" \
  -logFile "C:/Users/tgent/workspaces/sdf-geometry-generation/Temp/hermes-final-compile.log"
```

Expected: Exit-Code `0`, kein `error CS`, kein `Scripts have compiler errors`.

**Step 2: Rename- und Konfliktprüfung ausführen**

```bash
python Tools/verify_volume_rename.py
git diff --check
git diff --cached --check
git ls-files -u
```

Expected:

- Verification script: PASS
- beide `diff --check`: keine Ausgabe/Exit-Code `0`
- `git ls-files -u`: keine Ausgabe

**Step 3: Szenen-/Prefab-Integrität prüfen**

Über Unity-Log und GUID-Skript bestätigen:

- keine `The referenced script ... is missing`-Meldung für produktive Szenen/Prefabs;
- Composer-GUID löst auf `VolumeObjectRegistry.cs` auf;
- Model-GUID löst auf `VolumeProcessor.cs` auf;
- keine doppelte `.meta`-GUID.

**Step 4: Scope prüfen**

```bash
git status --short
git diff --stat
git diff --cached --stat
```

Expected: Keine unerwarteten Änderungen an `Library/`, `Temp/` oder generierter Chunk-Geometrie. Vorhandene Nutzeränderungen bleiben erhalten.

**Step 5: Finaler Commit**

```bash
git add Tools/verify_volume_rename.py Assets/Scripts/VolumeSystem/Composition Assets/Scripts/VolumeSystem/Editor Assets/Tests/Editor
git commit -m "fix: complete volume rename and restore unity compilation"
```

Nur noch nicht committed Fix-Dateien aufnehmen; keine pauschale Stage-Anweisung für das gesamte Repository verwenden.

---

## Tests und Abnahmekriterien

- [ ] `VolumeObject.cs` und alle relevanten Assets enthalten keine Merge-Konfliktmarker.
- [ ] `git ls-files -u` ist leer.
- [ ] `VolumeSceneComposer.cs` und `VolumeModel.cs` existieren nicht mehr als aktive Skripte.
- [ ] `VolumeObjectRegistry.cs.meta` verwendet die alte Composer-GUID `1db7fca8924706740a533d8d955ba297`.
- [ ] `VolumeProcessor.cs.meta` verwendet die alte Model-GUID `390c39fbad2c5a4468a59eeef512bf0d`.
- [ ] Alle produktiven Szenen/Prefabs lösen ihre Script-GUIDs auf vorhandene `.meta`-Dateien auf.
- [ ] Unity-Kompilierung endet mit Exit-Code `0` und ohne `error CS...`.
- [ ] Die Burst-Folgefehlermeldung über fehlendes `Assembly-CSharp-Editor` tritt nicht mehr auf.
- [ ] Gesamte aktivierte EditMode-Test-Suite besteht.
- [ ] `git diff --check` und `git diff --cached --check` bestehen.
- [ ] Keine nicht zusammenhängenden Nutzeränderungen wurden zurückgesetzt oder überschrieben.

## Risiken und Trade-offs

1. **Unity-GUID-Verlust:** Neue GUIDs an umbenannten MonoBehaviours würden vorhandene Komponenten als Missing Script erscheinen lassen. Deshalb ist GUID-Erhalt gegenüber massenhaften YAML-Edits zu bevorzugen.
2. **Großer Szenen-Diff:** `scene_outdoor_1.unity` enthält bereits umfangreiche Änderungen. Nur nachgewiesene Script-GUID-Felder anfassen; keine komplette Szene neu speichern, solange nicht notwendig.
3. **Historische Logfehler:** `Editor.log` enthält alte `CS8300`- und aktuelle `CS0246`-Blöcke. Verifikation muss mit Log-Offset oder Zeitstempel arbeiten, nicht mit einem globalen Grep über die gesamte Datei.
4. **Mehrere Unity-Prozesse:** Zweite Instanzen schlagen mit „another Unity instance is running“ fehl. Für Batch-Tests muss die interaktive Instanz vorher regulär geschlossen werden.
5. **Legacy-Code:** `VolumeModelOld.cs` steht hinter `#if LEGACY` und verweist auf alte APIs. Nicht unnötig modernisieren, solange `LEGACY` nicht als aktives Scripting Define gesetzt ist.
6. **Package-/Editor-Nebengeräusche:** Lizenz-Handshake, doppelte `Unsafe.dll`, Android-ADB-Scans und `MovementComponent.name`-Warnung sind nicht die aktuelle Compile-Root-Cause und sollen nicht in denselben Fix geraten.
7. **Zwischenzustand eines Merge/Rebase:** Falls ein Merge/Rebase aktiv ist, sind einzelne Commits eventuell nicht möglich. Dann dieselben logischen Checkpoints als getrennte Staging-/Verifikationsschritte beibehalten und erst am erlaubten Punkt committen.

## Offene Fragen, die während der Ausführung anhand des Repos beantwortet werden

- Enthält `scene_outdoor_1.unity` bereits temporäre neue Script-GUIDs, die gezielt zurückmigriert werden müssen?
- Ist `LEGACY` irgendwo in `ProjectSettings` als aktives Scripting Define gesetzt? Falls ja, muss `VolumeModelOld.cs` separat behandelt oder das Define bewusst entfernt werden.
- Gibt es nach erfolgreicher Runtime-Kompilierung eigenständige Editor-Assembly-Fehler, die bisher nur durch den primären `CS0246`-Fehler verdeckt werden?
- Soll `Tools/verify_volume_rename.py` dauerhaft als Regression-Guard bleiben oder nach Abschluss entfernt werden? Standardannahme: behalten, wenn es produktive Szenen/Prefabs zuverlässig schützt.
