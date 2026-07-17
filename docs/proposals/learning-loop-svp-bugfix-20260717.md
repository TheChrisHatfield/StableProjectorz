# Learning Loop Log — smart-value-paint bug hunt

**Feature:** `smart-value-paint` (T5–T10 surface)  
**Type:** `mine_apply` · **Cycles:** 100 condensed into 3 audit passes  
**Beacon:** `continual-learning/bugfix-agnostic-patterns.md` + integration wiring audit  
**Date:** 2026-07-17  

## Loop law

```text
Pattern checklist → SVP call-chain trace → FIX (one defect) → commit → review note → next
Revalidate: Pass1 wiring/false-success → Pass2 scaffold/atomic → Pass3 NaN/idempotent
Stop when a full pass finds no new defect on the SVP surface.
```

## Pass ledger

| Pass | Cycles (condensed) | Focus |
|------|-------------------:|-------|
| 1 | 1–35 | false-success, null/NRE, clobber overrides |
| 2 | 36–70 | scaffold≠wire, orphan identity, atomic arm state |
| 3 | 71–100 | idempotent UI build, non-finite sanitize, re-trace |

## Fixes (one commit each)

| # | Commit | Defect → Pattern |
|---|--------|------------------|
| 1 | `c1a1254` | Propose passed live brush hints → clobbered MLP cont heads → **opt-in overrides only** |
| 2 | `7bc970c` | Bias arrays unchecked → NRE after trunk OK → **validate whole network, then accept** |
| 3 | `5f703e8` | Orphan name without component → duplicate panel → **boundary-exact identity + repair** |
| 4 | `12c2961` | Failed Accept cleared saw-apply → **don't mutate arm telemetry until success** |
| 5 | `9c5bfd6` | Panel in git without CollectNow/MLP → **scaffold ≠ implementation** |
| 6 | `712a503` | BuildUi re-entry stacked chrome → **idempotent ownership-root build** |
| 7 | `b695ff0` | NaN width survived Clamp01 → **sanitize non-finite before ribbon mutate** |

## Saturation

Pass 3 re-trace (`TryAccept` → ribbon → `OnColorBrushApplied`, factory → MLP forward, EnsureUnder) found no further defects on the SVP surface. Remaining items are backlog (canvas-patch Propose, theme subscribe), not bugs.
