<!-- PROMOTED: cartridge mine — tertiary insights for smart-value-paint -->

# Cartridge insights — smart-value-paint

**Hook:** `planning.rosetta`, `compiler.pipeline`, `context.document_sourcing`  
**Status:** tertiary distillate (does **not** override Spec Kit)  
**Source:** `cartridge/source-context.md` ← `context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md` (132 chunks indexed; re-emitted)

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
| Paint Transformer as stroke-set baseline | Research note only |

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

1. SDXL generates style-consistent target paintings (optional variation).  
2. Quantize to 5/7 value bands → value-structure maps.  
3. Manufacture `(state → stroke/value decision)` pairs — **not** full-image generation by the MLP.  
4. Stage training: value-structure first, then stroke-parameter policy.

## [CTX:paint_transformer] Research cursor

Paint Transformer landed as stroke-set + synthetic self-training baseline; later work emphasizes planning “where next” and process reconstruction. Treat as **literature baseline**, not a Unity dependency.

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
