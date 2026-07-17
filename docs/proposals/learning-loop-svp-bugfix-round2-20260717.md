# Learning Loop Log — smart-value-paint bug hunt (round 2)

**Feature:** `smart-value-paint`  
**Type:** `mine_apply` · **Cycles:** 100 condensed into 3 audit passes  
**Beacon:** `continual-learning/bugfix-agnostic-patterns.md`  
**Date:** 2026-07-17 (round 2)  
**Prior:** [`learning-loop-svp-bugfix-20260717.md`](./learning-loop-svp-bugfix-20260717.md)

## Pass ledger

| Pass | Cycles | Focus |
|------|-------:|-------|
| 1 | 1–35 | compositing, empty tex, NaN overrides |
| 2 | 36–70 | validate-then-commit order, status wipe, custom alpha |
| 3 | 71–100 | false-healthy factory fallback; re-trace saturate |

## Fixes (one commit each)

| # | Commit | Defect → Pattern |
|---|--------|------------------|
| 1 | `fc8f976` | ColorBlock absolute RGB × Image.color → muddied buttons → **compositing model** |
| 2 | `06b4468` | Empty Texture2D Clamp max&lt;min → **guard empty before clamp** |
| 3 | `2a72c92` | NaN brush hints into MLP DTO → **sanitize overrides** |
| 4 | `9ea8c1a` | Hardness resolved after ribbon mutate → **validate then commit** |
| 5 | `1a72f48` | Success status overwritten by refresh → **don't wipe committed UX text** |
| 6 | `4a12fdf` | Accept stole custom alpha stamp → **never overwrite content-bearing selection** |
| 7 | `5e06415` | Silent MLP fallback looked healthy → **surface fallback reasons** |

## Saturation

Pass 3 re-trace found no further defects on the SVP Accept/Propose/MLP/extractor surface. Backlog unchanged (canvas-patch Propose, theme subscribe).
