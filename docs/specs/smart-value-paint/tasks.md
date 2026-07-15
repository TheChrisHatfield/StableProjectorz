<!-- PROMOTED: smart-value-paint — review before handoff -->

# Tasks: smart-value-paint

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../../planning-rosetta-stone.md) (`planning.rosetta`) before executing tasks.

- **Spec:** [`spec.md`](./spec.md)
- **Plan:** [`plan.md`](./plan.md)
- **Delta micro:** [`smart-value-paint.md`](../../delta/20_micro/smart-value-paint.md)

## Task 1 — Scaffold rename (complete)

- [x] Update holistic/macro docs for smart-value-paint
- [x] Create Delta micro + Spec Kit trio under `smart-value-paint`
- [x] Set active feature in `AGENTS.md`

## Task 2 — Paint-path discovery map (complete)

- [x] Trace caller → paint target → UV stroke apply for normal color paint
- [x] Document types/files for proposal sink (no behavior change yet) — see [`plan.md`](./plan.md) discovery map + Delta micro
- [x] Impact packet if/when code files are listed for edit — **N/A** (docs-only; defer to Task 3+)

## Task 3 — Proposal API + value-bin stub (complete)

- [x] Define proposal DTO / interface matching Spec R2 — `IValuePaintAssist`, `ValuePaintProposal`, bands/roles
- [x] Deterministic value-band stub from a patch or reference texture — `DeterministicValuePaintAssist`
- [x] Unit/editor-checkable path that does not write strokes yet — menu `StableProjectorz/Smart Value Paint/Run assist check`
- [x] Impact packet: `docs/change-impact/impact-packets/chg_20260715_030532_smart-value-paint.json` (risk low)

## Task 4 — Apply through existing paint stack (complete)

- [x] Wire “accept proposal” into existing stroke/layer path (R3) — `ValuePaintProposalApplier.TryAccept` → ribbon color/size/opacity; hook `OnColorBrushApplied` in `Inpaint_MaskPainter` after successful `Apply_into_ColorBrushTex`
- [x] Respect active layer + paint mode — requires `Inpaint_Color`, refuses NoColor/smudge; target must be `ActiveLayer.Content` when stack present
- [x] Integration-level validation evidence — menu `Try accept midtone proposal`; impact `chg_20260715_031053_smart-value-paint`

## After each implementation task — Integration wiring audit (required)

- [x] Traced caller → handler → core (no layer break) — Accept → `SD_WorkflowOptionsRibbon_UI` / `BrushRibbon_UI_Opacity` → user stroke → `Inpaint_MaskPainter.OnRenderIntoCurrTex_please` → `OnBeforeColorBrushApply` → `Apply_into_ColorBrushTex` on active Content
- [x] No false success when a sub-step failed or was skipped — `TryAccept` returns false with reason if workflow/mode/target/ribbon missing; Editor logs Warning on refuse
- [x] Integration-level validation evidence attached — Editor menus + impact packet `chg_20260715_031053_smart-value-paint`; Play Mode Inpaint_Color + paint stroke sets `SawApplyOnArmedTarget`
- [x] See `.cursor/rules/integration-wiring-audit.mdc`

## Active task

**None (feature tasks 1–4 complete)**
