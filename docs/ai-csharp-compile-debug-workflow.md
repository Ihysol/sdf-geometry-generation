# AI Workflow: Compile and Debug C# Errors in This Unity Project

Use this workflow when changing or debugging `.cs` files in this repository.

## Project Context

This is a Unity project, not a plain SDK-style .NET project.

- Unity version: `6000.4.1f1`
- Project path: `C:\Users\tgent\workspaces\sdf-geometry-generation`
- Main source path: `Assets\Scripts`
- Editor tests path: `Assets\Tests\Editor`
- Generated IDE files such as `Assembly-CSharp.csproj`, `Assembly-CSharp-Editor.csproj`, and `sdf-geometry-generation.slnx` are useful for navigation, but Unity is the authoritative compiler.

Do not rely on `dotnet build` as the final compile check for project `.cs` files. Use Unity batchmode or Unity Editor compilation.

## Before Debugging

1. Check the current branch and dirty files:

   ```powershell
   git status --short --branch
   ```

2. If `rtk` is available in the shell, prefix commands with it, for example:

   ```powershell
   rtk git status --short --branch
   ```

   If `rtk` is not installed or not on `PATH`, run the underlying command directly.

3. Do not revert unrelated user changes.

## Compile Check

Run Unity in batchmode from PowerShell:

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe"

& $unity `
  -batchmode `
  -nographics `
  -projectPath "C:\Users\tgent\workspaces\sdf-geometry-generation" `
  -quit `
  -logFile compile.log
```

Then extract the compiler-relevant lines:

```powershell
Select-String -Path compile.log `
  -Pattern "error CS\d+|warning CS\d+|Compilation failed|Assets.*\.cs\("
```

If Unity exits with a nonzero code, inspect `compile.log` before changing code.

## Debugging Rules

Follow this order:

1. Read the first real compiler error carefully.
2. Prefer the earliest `error CS...` in project code under `Assets\`.
3. Treat later errors as likely cascade errors until proven otherwise.
4. Find the referenced file, line, symbol, namespace, or assembly definition.
5. Compare against nearby working code in the same subsystem before editing.
6. Make the smallest change that addresses the root cause.
7. Re-run the Unity compile check.

Common Unity C# compile causes:

- Missing `using` directive.
- Type or method renamed in another file.
- Namespace mismatch.
- Runtime code referencing `UnityEditor` APIs outside an `Editor` folder or editor-only assembly.
- Editor test code referencing runtime internals without the right visibility or assembly reference.
- File/class name mismatch for `MonoBehaviour` scripts used by scenes or prefabs.
- API mismatch caused by generated `.csproj` files being stale relative to Unity compilation.

## Running Tests

After compilation succeeds, run EditMode tests when the change touches logic:

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe"

& $unity `
  -batchmode `
  -nographics `
  -projectPath "C:\Users\tgent\workspaces\sdf-geometry-generation" `
  -runTests `
  -testPlatform editmode `
  -testResults TestResults.xml `
  -logFile unity-test.log `
  -quit
```

Inspect failures:

```powershell
Select-String -Path unity-test.log `
  -Pattern "FAIL|Failed|error CS\d+|Exception| at Assets"
```

## Interactive Debugging

For runtime or Play Mode behavior:

1. Open the project in Unity `6000.4.1f1`.
2. In VS Code, use `.vscode\launch.json`.
3. Select `Attach to Unity`.
4. Set breakpoints in the relevant `.cs` files.
5. Reproduce the behavior in Play Mode or through the relevant Editor test.

## Important Notes for Another AI

- Unity batchmode compilation is the source of truth.
- Generated `.csproj` files may lag behind Unity state; do not overfit fixes to IDE-only analysis.
- Fix root causes, not cascaded compiler noise.
- Keep edits scoped to the failing subsystem.
- Re-run Unity compilation after every meaningful fix.
- Do not clean, delete, reset, or regenerate broad project state unless the user explicitly asks.
