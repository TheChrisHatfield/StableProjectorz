<!-- PROMOTED: smart-value-paint — review before handoff -->

<!-- BOOTSTRAP: hive_planner init -->

# Feature Micro Brief: smart-value-paint

**Hook:** `spec.flow`

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../../planning-rosetta-stone.md) (`planning.rosetta`) before coding from this brief.

- **Hooks:** `context.delta`, `spec.flow`
- **Spec Kit:** [`spec.md`](../../specs/smart-value-paint/spec.md), [`plan.md`](../../specs/smart-value-paint/plan.md), [`tasks.md`](../../specs/smart-value-paint/tasks.md)

## Feature Name

smart-value-paint — adaptive value-scale painting assist on StableProjectorz’s existing paint stack.

## Scope

| In scope | Out of scope |
|----------|--------------|
| Value-band analysis (highlight / light / mid / shadow / accent) of a reference or canvas patch | Full generative painting replacing the brush |
| Stroke / parameter proposals (bin, blend, softness, width/opacity hints, stroke role) | Production-ready MLP training farm in v1 |
| Integration points into Paint / Inpaint UV apply path | Mandatory Decimacon orchestration runtime |
| Spec/plan/tasks + wiring-audit gate before CL memory | Behavioral truth stored only in `AGENTS.md` |

## Paint-path discovery (Task 2 — verified)

Normal **color** paint chain (proposal sink candidates):

1. `Update_callbacks_MGR.brushing` → `MaskPainter.OnUpdate` / `OnPointerDown_maybe` / `PaintOnTexture`
2. `Inpaint_MaskPainter.OnRenderIntoCurrTex_please` → `GetPaintTarget()` → active `PaintLayer.Content`
3. `ApplyBrushStroke_ToUvMask.Apply_into_ColorBrushTex` (per-frame delta commit)
4. Stroke end: `Act_OnPaintStrokeEnd` (listeners only; color already written)

**Preferred sinks:** (1) before `Apply_into_ColorBrushTex` in `OnRenderIntoCurrTex_please`; (2) entry of `Apply_into_ColorBrushTex`; (3) `Act_OnPaintStrokeEnd` for deferred apply. Full table: [`plan.md`](../../specs/smart-value-paint/plan.md#discovery-map--normal-color-paint-task-2).

**Out of path for v1 color assist:** smudge router, soft-inpaint gen args, projections/background painters, PaintTab as stroke driver.

## Touch zones (code tasks)

- `Assets/_gm/Features/Paint/SmartValuePaint/` — proposal API + deterministic stub + `ValuePaintProposalApplier` (Task 3–4)
- `Assets/_gm/Features/Paint/Editor/SmartValuePaintAssistCheck.cs` — Editor propose/accept checks
- `Assets/_gm/Features/Paint/Inpaint/Inpaint_MaskPainter.cs` — `OnColorBrushApplied` verify hook after successful apply (Task 4)
- `Assets/_gm/Features/Paint/MaskPainter.cs`
- `Assets/_gm/Features/Paint/ApplyBrushStroke_ToUvMask.cs`
- `Assets/_gm/Features/Paint/Layers/` (`PaintLayerStack_MGR`, `PaintLayer`)
- Optional later: Paint tab UI for proposal review

## Validation

```powershell
py -3.11 -m hive_planner spec-drift-check
py -3.11 -m hive_planner ci-check
# After code tasks: impact packet + wiring audit per .cursor/rules/integration-wiring-audit.mdc
```

## Tertiary research note

Multipass cartridge orientation (meta loop 2026-07-15):

| Pass | Beacon | Use for SVP |
|------|--------|-------------|
| A | SMART_VALUE + Paint Transformer | Proposal DTO + dataset self-training ideas |
| B | Adaptive Routing + LAVD | Keep scheduler/allocator ≠ paint reasoner |
| C | MLP Decimacon ORIENT + EXTRA (+ DEV_1) | Staged hybrid / selective attention — **understand**, don’t ship DAG in v1 |

Active emit after force includes `MLP_DECIMACON_ORIENT.txt`, `MLP_DECIMACON_DEV_EXTRA.txt`, and SMART_VALUE. See [`cartridge-insights.md`](../../specs/smart-value-paint/cartridge-insights.md) and [`source-correlation.json`](../../../cartridge/mappings/source-correlation.json).

## Decimacon → SVP role map (orientation only)

| Decimacon idea | SVP v1 stand-in |
|----------------|-----------------|
| Fast control / scoring MLP | `DeterministicValuePaintAssist` → future MLP on `IValuePaintAssist` |
| Router / expert select | **BACKLOG** — not T5; do not pull MoS runtime |
| Shared latent vault | Not required for value-bin proposals |
| Selective self-attention modules | Optional later Stroke MLP / critic |
| Sequential stages | Spec Kit task stages + human accept (R3) |

Full Decimacon remains **out of scope** until Spec explicitly opens it.
