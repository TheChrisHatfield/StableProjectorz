# Paint undo — profiling and tunables

## Targets

- **Stroke end:** Time from pointer-up to return from `OnFinal_ApplyIncomingVals_intoMask` (excluding async readback completion). Watch `CopyTexture` + compute apply cost.
- **Undo:** Wall time until restore completes (single-frame vs amortized). Test **1024² × 1 UDIM** and **2048² × multi-UDIM**.

## Code tunables (`PaintUndo_MGR` / `PaintUndo_Scheduler`)

- `maxUndoDepth` — from settings (`Settings_MGR`).
- **Scheduler:** `baseBudgetMs`, `minBudgetMs`, `maxBudgetMs`, `minSlicesPerFrame`, `maxSlicesPerFrame`, `agingBoostPerSecond`, `agingMaxMultiplier`.
- **UCB learning (optional):** `useUcbBudgetSelection` — discrete arms map to effective budget multipliers.

## Suggested procedure

1. Unity Profiler: CPU **Main Thread** during stroke burst and during Ctrl+Z.
2. Increase UDIM count with fixed resolution; ensure undo depth × snapshot size fits RAM (Task Manager / Unity memory).
3. If hitches exceed ~2 ms/frame during amortized restore, lower `maxSlicesPerFrame` or `maxBudgetMs`.
