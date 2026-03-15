# Build stability (pattern-based learning)

Stored as part of **continual learning**: generalize from build failures into reusable patterns. Each incident should produce a **reusable pattern**, not just "fix X." When a build breaks, classify by layer, then apply or extend these patterns.

## How to use (continual learning)

1. **When the build fails**: Identify **which layer** failed (script/tooling vs Unity process vs code run during build). Use "Layers of failure" below.
2. **When applying a fix**: State the **general pattern** the fix instantiates (e.g. "minimize invocation surface") so future similar cases get the same treatment.
3. **When adding a new incident**: Add a short "Instance" with one-line cause and a **"→ Pattern:"** line. If the pattern already exists, add the instance under it; if not, add a new pattern and one concrete example. Avoid overfitting: prefer "any long inline script in a single shell argument" over "PowerShell -Command exactly."

## Layers of failure (diagnosis)

| Layer | What can break | How to check |
|-------|-----------------|--------------|
| **Script/tooling** | .bat, .ps1, shell invocation (length, parsing, env) | Does the crash happen before/hardly after "Launching Unity"? Check command length, `-File` vs `-Command`, env vars. |
| **Unity process** | Compile error, crash, or exit code from Unity | Check `build_output.txt`, Unity exit code. Did Unity start and run `-executeMethod`? |
| **Code during build** | Code that runs in batch (e.g. domain reload, executeMethod, or unexpected Awake) | Search for `InitializeOnLoad`, static constructors, and any code path that could run when `Application.isBatchMode` is true. Guard or prove it doesn't run. |

Always decide which layer failed first; then apply the matching pattern.

## Generalized patterns (derive from these)

### Pattern: Minimize invocation surface

- **Idea**: The command line (or single shell argument) from the host to the next process is a fragile surface: length limits, quoting, and parsing vary by OS/shell. Keep it small; put logic in files.
- **Concrete**: Don't put long inline PowerShell in .bat; use `-File script.ps1` and pass inputs via env vars or args. Same for other shells: prefer "call a script" over "one huge string."
- **Instance**: PowerShell crash during build → long `-Command "..."` in .bat. **→ Pattern:** Minimize invocation surface; moved logic to `build_runner.ps1`, .bat calls `powershell -File "%~dp0build_runner.ps1"`.
- **Instance**: Bat still crashing after moving to .ps1 → `Write-Progress` in PowerShell can crash when run from .bat or non-interactive console. **→ Pattern:** Minimize invocation surface / avoid fragile UI; removed `Write-Progress`, use only `Write-Host` for heartbeat; call PowerShell with `< NUL` from .bat so it doesn't wait on stdin (avoids hang/crash).
- **Instance**: Bat still crashing even after removing Write-Progress → PowerShell itself can be unstable when invoked from .bat (various host/console issues). **→ Pattern:** Minimize invocation surface; avoid PowerShell entirely for the build wait loop. Run Unity **directly from the .bat** with `"%UNITY_EXE%" -batchmode -quit -projectPath ... -executeMethod ... -logFile ...` and let cmd wait; no progress/heartbeat but no PowerShell = no PowerShell crash. Keep `build_runner.ps1` for reference or optional use.
- **Instance**: Bat began to crash as soon as icon-sizing refactor was added (BrushRibbon_UI_AlphaPicker: HeaderIconSize property, post-layout loop forcing folder icon sizeDelta). **→ Pattern:** Revert direct icon sizing; use fixed constant and no post-layout size forcing.
- **Instance**: Bat still crashes after reverts; user runs from PowerShell or double-click. **→ Pattern:** (1) Write debug steps via `cmd /c echo ...`; (2) provide minimal test bat; (3) run from cmd not PowerShell.
- **Instance**: Continual crashes with .bat. **→ Pattern:** Rebuild from ground up in **PowerShell**: single `build_for_testing.ps1` (paths via $PSScriptRoot, find Unity, remove lock, Start Process with ProcessStartInfo + WaitForExit, copy log, report). No Write-Progress, no long -Command. `.bat` is only a launcher: `powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_for_testing.ps1"` (and -Clean when arg is clean). Run by double-clicking the .bat or from PowerShell: `.\build_for_testing.ps1`.

### Pattern: Batch mode is a distinct environment

- **Idea**: Code that does I/O, UI, `FindObjectOfType`, or heavy layout can fail or hang if it ever runs during `-batchmode` (e.g. if something triggers Awake or an Editor path). Either prove it never runs in batch or guard it.
- **Concrete**: For entry points that might run in batch (Awake, or methods called from executeMethod/build), add `if (Application.isBatchMode) return;` before non-trivial work, or ensure the entry point is never invoked in batch.
- **Instance**: Defensive guards in `BrushAlphas_MGR.Awake` and `BrushRibbon_UI_AlphaPicker.RebuildGrid` so folder scan and UI layout don't run in batch. **→ Pattern:** Batch mode is a distinct environment; guard or avoid heavy work there.

### Pattern: Separate "build entry" from "rest of app"

- **Idea**: The only code guaranteed to run during a batch build is what `-executeMethod` invokes (e.g. `BuildForTesting.BuildWin64`) and whatever that calls. Keep that path minimal and free of scene/UI assumptions.
- **Concrete**: Build entry point should do build steps only (e.g. `BuildPipeline.BuildPlayer`), then exit. Don't depend on singletons, scene objects, or runtime managers unless they are build-specific.

## Rules (apply patterns; don't overfit)

1. **Build scripts**: Apply **minimize invocation surface**. No long inline PowerShell in .bat; use `-File` and a .ps1. Don't change .bat/.ps1 unless improving the build process itself.
2. **C# that might run in batch**: Apply **batch mode is a distinct environment**. Guard I/O, UI, and FindObjectOfType/layout when the code path could run with `Application.isBatchMode == true`.
3. **After a new build failure**: (1) Classify by layer (script vs Unity vs code-during-build). (2) Fix with the matching pattern. (3) Add one "Instance" line and **→ Pattern:** under the right pattern (or create a new pattern if it's a new class of cause). (4) Prefer extending a general principle over adding a one-off rule.

## Quick reference (pattern → action)

| Situation | Pattern | Action |
|-----------|---------|--------|
| Adding or changing how the build is launched | Minimize invocation surface | Keep the shell command short; put logic in a script file. |
| Adding code that runs at load or in Awake/Start | Batch mode is a distinct environment | Guard with `Application.isBatchMode` or prove it never runs in batch. |
| Build "stops" or "crashes" | Layers of failure | Identify script vs Unity vs code-in-batch; then apply the pattern for that layer. |

Update this doc when new failures occur: add the **instance** and the **derived pattern** so the same class of problem is recognized and handled next time.
