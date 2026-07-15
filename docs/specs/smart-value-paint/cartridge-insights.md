<!-- PROMOTED: cartridge mine — tertiary insights for smart-value-paint -->

# Cartridge insights — smart-value-paint

**Hook:** `planning.rosetta`, `compiler.pipeline`, `context.document_sourcing`  
**Status:** tertiary distillate (does **not** override Spec Kit)  
**Sources (indexed):** SMART_VALUE_PAINT_DEV_1 · learning-loop-rosetta · `source4s/PAINT_Transformer.pdf` · `source4s/ADAPTIVE_ROUTING.pdf` (emit 2026-07-15; 4 docs / 241+ chunks)

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

1. [`docs/planning-rosetta-stone.md`](../../planning-rosetta-stone.md) — `planning.rosetta`
2. Delta — `context.delta`: [`00_holistic.md`](../../delta/00_holistic.md), [`10_macro.md`](../../delta/10_macro.md), [`smart-value-paint.md`](../../delta/20_micro/smart-value-paint.md)
3. Spec Kit — `spec.flow`: [`spec.md`](./spec.md), [`plan.md`](./plan.md), [`tasks.md`](./tasks.md)
4. This file — cartridge tertiary only; promote into Spec before implementation

## Gap: already shipped vs research

| Research idea (tertiary) | Current fork (`spec.flow` Tasks 1–4) |
|--------------------------|--------------------------------------|
| 5-group value ladder | Done — `ValuePaintBand` |
| MLP predicts bin / blend / edge / size / opacity / role | Partial — deterministic stub fills same DTO; **MLP weights not wired** |
| Feed predictions into existing UV/color path | Done — `TryAccept` → ribbon → `Apply_into_ColorBrushTex` |
| Stroke telemetry extractor above MaskPainter | **Not started** |
| SDXL as curated target/dataset generator | **Not started** (explicitly out of scope v1) |
| Separate Tonal MLP vs Stroke MLP vs Critic | **Not started** |
| Decimacon orchestration | Out of scope v1 (spec) |
| Paint Transformer as stroke-set baseline | Literature PDF ingested — **not** a runtime dep |
| Learned expert router (MoS / ASA) | Analogy only — must **not** reopen Decimacon v1 |

## [HOOK:context.document_sourcing] Architecture split (from cartridge)

| Layer | Function | Promote? |
|-------|----------|----------|
| SDXL | Target image / style prior / patch guidance | Later dataset pipeline (`spec.flow` open Q) |
| Decimacon controller | Route painting experts | Keep **out of scope** unless Delta changes |
| Tonal MLP | Value group + transition strength | **Next** when user provides MLP model → implement `IValuePaintAssist` |
| Stroke MLP | Stroke geometry + brush params | After tonal MLP stable |
| Critic | Score whether a stroke helped value structure | Later |
| Renderer / forge paint path | Existing UV layer apply | **Already sink** — do not fork |

## [HOOK:spec.flow] MLP contract (labels already match our DTO)

Cartridge labels map 1:1 to `ValuePaintProposal`:

- Value group / ΔValue → `CurrentBin` / `DesiredBin` (+ luminance)
- Edge hardness → `EdgeSoftness01` (**accept does not yet set hardness UI**)
- Blend strength → `BlendStrength01` (now applied as `opacity × blend` on accept)
- Brush width/opacity → hints (armed on ribbon)
- Stroke role → `ValuePaintStrokeRole`

**Inputs research wants next (not in stub yet):**

- Local canvas + reference patches
- Luminance histogram / edge magnitude
- Depth / normal / visibility cues from forge RTs
- Stroke history summary (last *n* deltas, pressure, velocity, angle)
- Optional semantic/region mask

## [HOOK:context.delta] Integration path (aligns with Task 2 map)

Cartridge recommends insertion **above** the brush shader:

1. Telemetry from `MaskPainter` update loops  
2. `StrokeFeatureExtractor` over existing RTs  
3. Model → proposal  
4. Feed into UV / color layer path (**already** `ValuePaintProposalApplier`)

Do **not** invent a parallel painter.

## [HOOK:compiler.pipeline] Dataset recipe (for when MLP arrives)

**Canonical write-up:** [`mlp-dataset-recipe.md`](./mlp-dataset-recipe.md) (learning-loop beacon 2026-07-15).

1. SDXL/artist targets → quantized 5-band value maps (optional SDXL).  
2. Manufacture `(state → decision)` rows matching `ValuePaintProposal` — **not** full images.  
3. Teachers: heuristics / forge telemetry / human accepts.  
4. Train bin head first, then multi-head params; export → T5.  
5. Self-training on in-progress canvases (Paint Transformer analogue, value-aware).

## [CTX:paint_transformer] Literature — Paint Transformer (source4s)

**Paper:** *Paint Transformer: Feed Forward Neural Painting with Stroke Prediction* (Baidu / NJU / Rutgers)  
**Path:** `context-library/sources/source4s/PAINT_Transformer.pdf`

| Claim | Loop outcome for SVP |
|-------|----------------------|
| Stroke generation as **set prediction** (feed-forward), not RL step-by-step | CONFIRM vs research thread; do **not** replace `IValuePaintAssist` DTO with Transformer set params for T5 |
| Self-training / no off-the-shelf stroke dataset | BACKLOG → informs T8 dataset recipe only |
| Parallel stroke-set inference | BACKLOG — after tonal MLP; optional Stroke-MLP / critic path |

**Lock:** measured paint-stack sink > paper architecture > naming.

## [CTX:adaptive_routing] Literature — Adaptive Routing / MoS (source4s)

**Paper:** *Mixture-of-Schedulers: An Adaptive Scheduling Agent as a Learned Router for Expert Policies*  
**Path:** `context-library/sources/source4s/ADAPTIVE_ROUTING.pdf`

| Claim | Loop outcome for SVP |
|-------|----------------------|
| Learned **router** picks expert policy at runtime | META-ONLY analogy to Tonal vs Stroke experts |
| Offline pattern model + fast expert switch | Must **not** promote Decimacon / MoS runtime (locked OOS v1) |

**Conflict rule:** if narrative wants a runtime router before MLP weights exist → CONFLICT → drop; precedence favors measured `TryAccept` sink.

## [CTX:paint_transformer_legacy] Research cursor (SMART_VALUE thread)

Paint Transformer ideas also appear in SMART_VALUE_PAINT_DEV_1; later work emphasizes planning “where next.” Treat PDF + thread as **literature baseline**, not a Unity package.

## [HOOK:learning.loop] Multipass triangulation (meta 2026-07-15)

| Pass | Sources | New insight for Delta |
|------|---------|------------------------|
| A | SMART_VALUE + Paint Transformer | Small decision heads + optional later stroke-set / self-training |
| B | Adaptive Routing + LAVD_* | Scheduler allocates; model reasons — keep split |
| C | MLP Decimacon ORIENT + EXTRA + DEV_1 | Staged hybrid, shared latent, selective attention — **orientation**, not v1 ship |

**Combined Delta laws:** sink wins · small heads first · scheduler ≠ reasoner · Decimacon family OOS v1 · tertiary ≠ Spec.

Role map (orientation): Base/Router/Gate/Cortex → SVP stand-ins documented in [`docs/delta/20_micro/smart-value-paint.md`](../../delta/20_micro/smart-value-paint.md).

## Recommended follow-on Spec Kit tasks (draft — not active until you confirm)

| ID | Task | Hook |
|----|------|------|
| T5 | Wire user-provided MLP behind `IValuePaintAssist` (same DTO; no stroke write in inference) | `spec.flow` |
| T6 | `StrokeFeatureExtractor` sampling canvas/value/edge (+ optional depth/normal) | `context.delta` |
| T7 | Map `EdgeSoftness01` → `BrushRibbon_UI_Hardness` on accept | `spec.flow` |
| T8 | Dataset/export harness for forge strokes → training rows (optional; after MLP path works) | `compiler.pipeline` |

## Rosetta index (this mine)

- `planning.rosetta` → this nav + legend  
- `context.delta` → Delta micro + integration path above `MaskPainter`  
- `spec.flow` → promote T5–T8 into `tasks.md` only after review  
- `integration.cl_spec` → keep MLP behavior in Spec; operational paths only in `AGENTS.md`  
- `compiler.pipeline` / `context.document_sourcing` → cartridge + SMART_VALUE_PAINT_DEV_1  
- `change.impact` → packet before editing `_gm` for T5+
