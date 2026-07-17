# Deep wiring trace — smart-value-paint (2026-07-17)

**Hook:** `integration.wiring_audit`, `spec.flow`, `runtime.tab_panel`  
**Scope:** Every hop from Paint tab open → Propose/Accept → UV color commit → SawApply UI  
**Prior shallow audit:** [`svp-wiring-audit-20260717.md`](./svp-wiring-audit-20260717.md)

---

## 1. Mount path (panel appears)

| Hop | Code | Evidence |
|-----|------|----------|
| 1 | `CommandRibbon_UI.Awake` → `EnsurePaintTabExists` | Creates tab+panel if needed; runtime panel gets `SetCreateSectionsIfMissing(true)` + `PaintTab_CollectPaintUI` |
| 2 | `PaintCollect_WaitForSingletons_crtn` | Polls ≤15s calling `CollectNow` until `IsFullyCollected` |
| 3 | Paint tab shown → panel `OnEnable` | `PaintTab_CollectPaintUI.OnEnable` → `CollectNow()` again |
| 4 | `CollectNow` scaffolding | **Was:** only if `BrushPresetsSection == null`. **Fixed:** any of 5 section refs null → `SetCreateSectionsIfMissing(true)` (matches `PAINT_TAB_SCAFFOLDING_AUDIT.md`) |
| 5 | Tool Options + Value Assist | `EnsureUnder(ToolOptionsSection)` after tool-options row create/resubscribe |
| 6 | Buttons | Propose / Accept / Dismiss → `OnPropose` / `OnAccept` / `OnDismiss` |

**Break if unfixed:** prefab with BrushPresets assigned but ToolOptions null → Value Assist never mounts (scaffold ≠ impl).

---

## 2. Propose path (model → DTO)

| Hop | Code | Notes |
|-----|------|-------|
| 1 | `EnsureAssist` → `ValuePaintAssistFactory.Create` | MLP first; fallback surfaces `mlp unavailable: …` |
| 2 | `Resources.Load("SmartValuePaint/multihead_weights")` | File: `…/Resources/SmartValuePaint/multihead_weights.json` |
| 3 | `ValuePaintMlpRuntime.Validate` | Expects 448/64/2048/32 + head tensors — measured match |
| 4 | `ProposeFromColor(brush, default)` | No `HasBrushHints` (opt-in override law) |
| 5 | UI | Summary + desired-band swatch + Accept enabled |

**Not wired (backlog):** canvas/`ActiveLayer.Content` patch sample — Propose is brush-color only.

---

## 3. Accept path (DTO → ribbon)

| Hop | Gate / mutate | False-success guard |
|-----|---------------|---------------------|
| Mode | `isMode_using_img2img` + exact `Inpaint_Color` | Refuse NoColor / projections / etc. |
| Tool | `!isSmudge`, `isPositive` | Refuse smudge & erase |
| Target | `ResolveColorPaintTarget` | ActiveLayer.Content required when stack+layer; else fallback buffer |
| Opacity | `BrushRibbon_UI_Opacity` must exist | Refuse **before** color mutate |
| Size | `BrushRibbon_UI_Size.instance` must exist | **Fixed:** refuse before color (avoid NRE/half-apply on `sd.SetBrushSize`) |
| Hardness | Resolve UI before mutate | `TrySetBuiltInOnly` skips custom alpha |
| Mutate | color → size → opacity×blend → hardness | Then `_armed=true`, reset saw-apply |
| Fail path | Early returns | Do **not** clear prior armed telemetry |

**Stamp wire:** next stroke uses `BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback()` after hardness index change.  
**Color wire:** `Apply_into_ColorBrushTex` reads `SD_WorkflowOptionsRibbon_UI.brushColor` each dispatch.  
**Opacity wire:** `maskBrushOpacity` / `_MaxPossibleBrushStrength01` from opacity UI.

---

## 4. Stroke commit path (armed → pixels → SawApply)

```
MaskPainter.OnUpdate / brushing
  → Inpaint_MaskPainter.OnRenderIntoCurrTex_please
  → GetPaintTarget()  // Content (Color) or NoColorMask (NoColor) or scene fallback
  → [smudge branch]  // NO OnColorBrushApplied — Accept already refuses smudge
  → [color branch]
       CanDispatch_ColorBrushTex?
       → Apply_into_ColorBrushTex(...) == true
       → ValuePaintProposalApplier.OnColorBrushApplied(target)
            → ResolveColorPaintTarget; ReferenceEquals(expected, destin)
            → SawApplyOnArmedTarget = true
  → PaintTab_ValueAssistPanel_UI.Update
       → RefreshStatusLine when SawApply and status lacks "stroke applied"
```

| Path | SawApply? |
|------|-----------|
| Color paint, armed, destin == Content | Yes |
| Apply returns false (no chunks/kernel) | No (correct — no false SawApply) |
| Smudge | No (separate branch) |
| Armed then switch to NoColor | destin=NoColorMask ≠ Content → No |
| Erase (`isPositive` false) | Accept refused; if somehow armed, sign negative still goes color path — edge case |

---

## 5. Alternate / editor path

| Entry | Chain |
|-------|-------|
| Menu Run assist check | Factory → Propose* (no stroke) |
| Menu Run MLP assist check | `MlpValuePaintAssist.TryCreate` only |
| Menu Try accept midtone | Factory → `TryAccept` (refuse OK in Edit mode) |
| Menu Clear armed | `ClearArmed` |

---

## 6. Failure-class results (deep)

| Class | Result |
|-------|--------|
| Scaffold ≠ impl | **FIXED** — CollectNow any-section create gate restored |
| Layer break | Mount→Propose→Accept→Apply→SawApply→UI all call-linked |
| False success | Size null refuse; Apply false skips SawApply; hardness skip annotated |
| Dead config | Weights path + tensor lengths live |
| Half-apply | Color-before-size NRE path closed by size instance check |
| Tab/panel | `EnsurePaintTabExists` follows both-must-exist skip rule |

---

## 7. Remaining intentional gaps

1. Propose from canvas luminance patch (not brush color)  
2. Theme subscribe on ValueAssist chrome  
3. `FindObjectOfType` for Opacity/Hardness (multi-instance risk; same pattern as rest of Paint tab)

## Verdict

**PASS** after scaffolding gate + size-null Accept guard.
