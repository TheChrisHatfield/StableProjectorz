<!-- PROMOTED: smart-value-paint — review before handoff -->

# Tasks: smart-value-paint

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../../planning-rosetta-stone.md) (`planning.rosetta`) before executing tasks.

- **Spec:** [`spec.md`](./spec.md)
- **Plan:** [`plan.md`](./plan.md)
- **Delta micro:** [`smart-value-paint.md`](../../delta/20_micro/smart-value-paint.md)
- **Dataset:** [`mlp-dataset-recipe.md`](./mlp-dataset-recipe.md) · [`dataset-curation-plan.md`](./dataset-curation-plan.md) · [`mlp-train-spec.md`](./mlp-train-spec.md) (**T9**)

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

- [x] Traced caller → handler → core (no layer break) — Accept → `SD_WorkflowOptionsRibbon_UI` / `BrushRibbon_UI_Opacity` → user stroke → `Inpaint_MaskPainter.OnRenderIntoCurrTex_please` → `Apply_into_ColorBrushTex` → `OnColorBrushApplied` on active Content
- [x] No false success when a sub-step failed or was skipped — `TryAccept` returns false with reason if workflow/mode/target/ribbon missing; Editor logs Warning on refuse
- [x] Integration-level validation evidence attached — Editor menus + impact packet `chg_20260715_031053_smart-value-paint`; Play Mode Inpaint_Color + paint stroke sets `SawApplyOnArmedTarget`
- [x] See `.cursor/rules/integration-wiring-audit.mdc`

## Task 8 — Dataset curation harness (complete)

**Confirmed:** 2026-07-15 — [`dataset-curation-plan.md`](./dataset-curation-plan.md) + Paint Transformer §1b laws.  
**Harness root:** `tools/smart-value-paint-dataset/`  
**Exit (P0):** 50+ targets metadata, 500 Stage-1 rows, 500 Stage-2 rows, balance report green (loose).  
**P2 corpus (2026-07-17):** ~4008 keepers · Stage-1 ~46k · Stage-2 ~171k · `FINAL_REVIEW_P2.md` gates PASS.

- [x] **T8.1** Diversity prompt/tag pack YAML covering subject × lighting × contrast cells — `tools/smart-value-paint-dataset/diversity_matrix.yaml`
- [x] **T8.2** Target batch driver (Comfy MCP / `generate_image`) with sidecars: seed, model, WxH, prompt hash, tags — `scripts/batch_targets.py` (smoke `runs/p0_smoke/` 4/4 OK)
- [x] **T8.3** Value-map + multi-scale patch extractor → Stage-1 JSONL — `scripts/extract_stage1.py` / `value_map.py` (48 rows on `p0_smoke`)
- [x] **T8.4** Teachers: heuristic + **Sb/Sf residual self-train** + residual gate; emit Stage-2 rows — `scripts/teachers.py` / `extract_stage2.py` (63 rows on `p0_smoke`)
- [x] **T8.5** Balance / QA report (gates in curation plan §F) + train/val/test split by `target_id` — `scripts/balance_report.py` (`balance_report.md` / `splits/`)
- [x] **T8.6** P0 smoke documented + scaled — `docs/proposals/svp-dataset-p0-smoke-20260715.md` · `runs/p0/` (Flux keepers; Stage-1/2 rebuilt)
- [x] **T8.7** Role rebalance — teacher same-band ≠ BlockIn; Soften thresholds; iterative `stratify_roles` so **no role > 35%** — `teachers.py` / `extract_stage2.py --role-cap`

**Not in T8:** T5 MLP weights · Decimacon farm · Paint Transformer runtime · medical stroke datasets.

## Task 9 — Train MLP on T8 JSONL (ACTIVE)

**Confirmed:** 2026-07-17 — [`mlp-train-spec.md`](./mlp-train-spec.md)  
**Corpus:** `tools/smart-value-paint-dataset/runs/p0/` (full P2)  
**Order:** T9.1 bin head → T9.2 multi-head → T9.3 export layout for T5.

- [x] **T9.1** Stage-1 bin classifier (`current_bin` / `desired_bin`) on `splits/stage1_*.jsonl` — features = 7 floats (§2 train spec); metrics + `runs/p0/models/` (`bin_head.pt`, `metrics_t9_1.json` — val_des≈0.982, test_des≈0.976, exit PASS)
- [x] **T9.2** Stage-2 multi-head (role + continuous params) on `splits/stage2_*.jsonl` — `multihead.pt`, `metrics_t9_2.json` (val role≈0.691, cont_mae≈0.079, exit PASS; soft role-cap pred share ~36%)
- [x] **T9.3** Write `feature_layout.json` + `export_proposal_map.md`; handoff ready for **T5**

**Deferred (documented, not blocking):** human-accept logger · history/depth features · hard non-Comfy holdout · ONNX.

## Task 5 — Wire MLP behind IValuePaintAssist (complete)

**Confirmed:** 2026-07-17 — T9 weights → CPU runtime.  
**Impact:** `docs/change-impact/impact-packets/chg_20260717_mlp_t5_smart-value-paint.json`

- [x] Export `multihead.pt` → Resources JSON — `scripts/export_unity_weights.py`
- [x] `ValuePaintMlpRuntime` + `MlpValuePaintAssist` (7-float features, proposal-only)
- [x] `ValuePaintAssistFactory` prefers MLP; falls back to deterministic stub
- [x] Editor: **StableProjectorz / Smart Value Paint / Run MLP assist check**
- [x] Accept path unchanged — `TryAccept` still the stroke sink

## Task 6 — StrokeFeatureExtractor

**Impact:** `docs/change-impact/impact-packets/chg_20260717_t6_t7_smart-value-paint.json`

- [x] `StrokeFeatureExtractor` — patch hist + 2D edge magnitude → locked 7-float features
- [x] `ValuePaintFeatureBuilder.FromColors` / `MlpValuePaintAssist.ProposeFromColors` use extractor
- [x] Optional `TryExtractFromTexture` for CPU-readable Texture2D regions

## Task 7 — EdgeSoftness → hardness UI

- [x] `ValuePaintProposalApplier.TryAccept` maps `EdgeSoftness01` → `BrushRibbon_UI_Hardness.SetBuiltInOnly` (0 soft / 1 med / 2 hard)
- [x] `Softness01ToHardnessIx` public helper (≥0.66→0, ≥0.33→1, else 2)

## Task 10 — Paint-tab proposal UI

**Impact:** `docs/change-impact/impact-packets/chg_20260717_t10_paint-tab-value-assist-ui.json`

- [x] `PaintTab_ValueAssistPanel_UI` — Propose / Accept / Dismiss under Tool Options
- [x] Propose from brush color + stroke hints; Accept → `ValuePaintProposalApplier.TryAccept`
- [x] Wired from `PaintTab_CollectPaintUI.CollectNow` via `EnsureUnder`
- [x] Settings store + on/off + blend/size/opacity influence + neural/hardness toggles (`PaintTab_ValueAssistOptions`)
- [x] Live under-cursor predict (`ValuePaintLivePredictor` + `Inpaint_MaskPainter` GPU sample → quiet ribbon arm)

## Active task

**None** — T5–T7 + T9 + T10 proposal UI complete.

## BACKLOG — Spec-AC LAVD into SVP (Pass B)

**Status:** Formal backlog — soil-only until opened.  
**Surface:** `tools/mlp-decimacon-soil/src/hive_code_dev_1/lavd/`  
**Locks (do not violate while backlog):**

- Scheduler ≠ paint reasoner — `refuse_bandit_to_paint_dto` / `paint_boundary.py`
- Bandit → `ValuePaintProposal` fields = **locked drop**
- Unity `Assets/_gm` does **not** import soil LAVAD (intentional)

| ID | Item | Notes |
|----|------|-------|
| LAVD-AC.1 | Spec-AC LAVD runtime beside paint | Open only when Pass B product decision lands |
| LAVD-AC.2 | Idle / pen-up exploration in collector | Stub: `lavd/idle_explore.py` |
| LAVD-AC.3 | Real OS hybrid P/E topology | Stub: `lavd/hybrid_topology.py` (`TopologySource.OS_REPORTED` reserved) |
| LAVD-AC.4 | EXTRALAVD 3-arm ↔ soil 4 `BanditArm` | Map-only: `lavd/extralavd_arm_map.py` |
