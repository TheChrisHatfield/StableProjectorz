# Wiring audit — smart-value-paint (2026-07-17)

**Hook:** `integration.wiring_audit`, `change.validation`, `spec.flow`  
**Feature:** smart-value-paint (T5–T10 + bugfix rounds)

## Chains traced

### A — Paint-tab Propose → Accept → stroke

```
CommandRibbon_UI / PaintTab OnEnable
  → PaintTab_CollectPaintUI.CollectNow()
  → PaintTab_ValueAssistPanel_UI.EnsureUnder(ToolOptionsSection)
  → Propose: ValuePaintAssistFactory → MlpValuePaintAssist|Deterministic
             Resources.Load("SmartValuePaint/multihead_weights")
  → Accept: ValuePaintProposalApplier.TryAccept
             → SD_WorkflowOptionsRibbon_UI.SetBrushColorFromApi / SetBrushSize
             → BrushRibbon_UI_Opacity.SetOpacity01
             → BrushRibbon_UI_Hardness.TrySetBuiltInOnly
  → Paint: Inpaint_MaskPainter → Apply_into_ColorBrushTex (success)
             → ValuePaintProposalApplier.OnColorBrushApplied
  → UI: ValueAssist Update() surfaces SawApplyOnArmedTarget
```

### B — Editor non-UI path

```
Menu StableProjectorz/Smart Value Paint/*
  → Factory / MlpValuePaintAssist.TryCreate / TryAccept / ClearArmed
```

## Failure-class checklist

| Class | Result |
|-------|--------|
| Scaffold ≠ implementation | **PASS** — EnsureUnder in CollectNow (HEAD `9c5bfd6`); MLP JSON under Resources; panel + applier committed |
| Layer break | **FIXED** — SawApply was set but panel never refreshed until re-enable; Update() now surfaces it |
| False success | **PASS** — TryAccept refuses with reason; hardness skip noted (`customAlpha` / missing UI); factory surfaces MLP load error |
| Dead config | **PASS** — Resources path matches `Resources/SmartValuePaint/multihead_weights.json`; tensor lengths match Validate() (7/64/32) |
| Test theater | **PASS** — Editor menus call real Factory/TryAccept; refuse logged as warning |

## Evidence

- Call-site grep: EnsureUnder, TryAccept, OnColorBrushApplied, TrySetBuiltInOnly, Factory
- Weights: trunk0 448, trunk1 2048, heads 160×3, cont 128 + biases — match runtime Expect()
- `git ls-files` includes panel, MLP stack, Resources JSON, CollectPaintUI EnsureUnder

## Gaps (non-blocking backlog)

- Propose still samples brush color, not canvas patch under cursor
- No theme subscribe on ValueAssist chrome (cosmetic)

## Verdict

**PASS** after SawApply→UI wire fix.
