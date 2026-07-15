<!-- PROMOTED: smart-value-paint — review before handoff -->

# Plan: smart-value-paint

**Hook:** `spec.flow`  
**Spec:** [spec.md](./spec.md)

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../../planning-rosetta-stone.md) (`planning.rosetta`) before editing this plan.

- **Spec:** [`spec.md`](./spec.md)
- **Delta micro:** [`smart-value-paint.md`](../../delta/20_micro/smart-value-paint.md)
- **Hooks:** `spec.flow`, `context.delta`

## Approach

1. **Map fork paint path** — Done (Task 2); see discovery map below.
2. **Define proposal DTO** — Done (Task 3): `IValuePaintAssist` / `ValuePaintProposal` under `Assets/_gm/Features/Paint/SmartValuePaint/`.
3. **Value map producer** — Done for v1 stub: Rec.709 luminance → five bands in `DeterministicValuePaintAssist` (SDXL/Forge optional later).
4. **Assist service stub** — Done: deterministic baseline; MLP may implement same interface later (R5).
5. **Apply path** — Done (Task 4): `ValuePaintProposalApplier.TryAccept` arms ribbon params; live strokes still go through `Apply_into_ColorBrushTex` on `ActiveLayer.Content`.
6. **Validation gates** — Impact packet before non-trivial code edits; integration wiring audit after each implementation task; `spec-drift-check` before CL promotion.

## Discovery map — normal color paint (Task 2)

`Inpaint_MaskPainter` is the color-paint engine for img2img / Inpaint_Color. Soft inpaint args are **gen-request only** — not on the live stroke path.

### Call chain

| # | Type / method | File | Role |
|---|---------------|------|------|
| 0 | `Update_callbacks_MGR.brushing` → `MaskPainter.OnUpdate` | `Assets/_gm/Features/Paint/MaskPainter.cs` | Stroke driver (not Unity `Update`) |
| 1 | `OnPointerDown_maybe` | same | LMB/pen down + `isAllowedToPaintNow` → start stroke, init R8 path arrays |
| 2 | `OnDrag_maybe` → `PaintOnTexture` | same | Size / spacing / stamp / symmetry → shaders |
| 3 | `OnRenderIntoCurrTex_please` | `Assets/_gm/Features/Paint/Inpaint/Inpaint_MaskPainter.cs` | Inpaint override; mesh → curr stroke R8 |
| 4 | `GetPaintTarget` | same | Active layer `PaintLayer.Content` (fallback `_ObjectUV_brushedColorRGBA`); NoColor → `NoColorMask` (out of scope for normal color) |
| 5 | `Apply_into_ColorBrushTex(..., useBrushStrokeDelta: true)` | `Assets/_gm/Features/Paint/ApplyBrushStroke_ToUvMask.cs` | Per-frame commit into target `RenderUdims` via compute (`BLEND_RGBA_ONCE` + delta) |
| 6 | `OnPointerUp_maybe` → `OnFinal_ApplyIncomingVals_intoMask` → `Act_OnPaintStrokeEnd` | MaskPainter → Inpaint | Color already written per frame; final = re-render + stroke-end listeners |
| 7 | Display composite | layer Content → UV accumulation | Live stroke overlay / re-render |

**Param sources (not stroke drivers):** `BrushRibbon_UI_Size`, `BrushAlphas_MGR`, `SD_WorkflowOptionsRibbon_UI` (`brushColor`, opacity, erase sign).

**Gate:** `Inpaint_MaskPainter.isAllowedToPaintNow` — UsualView, `dim_sd`, img2img workflow, not eyedropper/select/blocker.

### Not on this path

- Smudge → `Apply_smudge_to_ColorBrushTex` / `SmudgeStrokeRouter`
- Soft inpainting → SD payload only
- `Projections_MaskPainter` / `Background_Painter`
- PaintTab / BrushRibbon — settings + tool mode only

### Proposal-sink hooks (for Task 4; do not hitch to smudge)

| Priority | Hook | Use |
|----------|------|-----|
| 1 | Immediately before `Apply_into_ColorBrushTex` inside `OnRenderIntoCurrTex_please` | Override live-stroke params (color, sign, opacity, target) on the mutating frame; undo capture already sits above |
| 2 | Entry of `ApplyBrushStroke_ToUvMask.Apply_into_ColorBrushTex` | Narrow GPU sink; branch at caller if inpaint-only |
| 3 | `OnFinal_ApplyIncomingVals_intoMask` / `Act_OnPaintStrokeEnd` | Deferred / post-stroke proposal apply; do not treat mouse-up as the primary color write |

**Secondary:** `GetPaintTarget()` if a proposal needs a different layer/buffer.

### Impact packets

- Task 2: docs-only — none.
- Task 3: `docs/change-impact/impact-packets/chg_20260715_030532_smart-value-paint.json` (low) for `Assets/_gm/Features/Paint/SmartValuePaint/`.
- Task 4: `docs/change-impact/impact-packets/chg_20260715_031053_smart-value-paint.json` (low) including `Inpaint_MaskPainter.cs`.

## Risks

- Research text may name systems not present in `_gm`; always verify types in-fork.
- Over-scoping MLP training before a proposal sink exists.

## Success for this plan revision

Tasks 1–4 complete (scaffold, discovery, proposal API, accept → existing paint stack).
