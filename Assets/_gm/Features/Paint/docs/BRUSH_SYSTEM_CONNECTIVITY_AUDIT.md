# Brush system connectivity audit

This document confirms how Stable Projectorz **fundamentally** handles brushes after the refactor: one source of truth for size/spacing and stamp, and full connectivity from brush selection to paint so there are no gaps or duplicate paths.

---

## 1. Canonical sources (single source of truth)

| What | Canonical source | App-wide read | Used by |
|------|------------------|---------------|---------|
| **Brush size** (0–1) | `BrushRibbon_UI_Size.instance` | `BrushRibbon_UI_Size.GetBrushSize01()` | MaskPainter (visible size, cursor, stroke), AlphaPicker labels, Save/Load |
| **Brush spacing** (0–1) | `BrushRibbon_UI_Size.instance` | `BrushRibbon_UI_Size.GetBrushSpacing01()` | Save/Load; stroke use (when we add spacing to the pipeline) |
| **Brush stamp** (texture) | `BrushAlphas_MGR` (current entry) | `BrushAlphas_MGR.GetCurrentBrushStampTex()` | Inpaint_MaskPainter, Projections_MaskPainter, ProjectorCameras_RenderHelper |
| **Selected brush index** | `BrushAlphas_MGR.CurrentIndex` | `BrushAlphas_MGR.CurrentIndex` | Hardness UI, Save/Load (maskBrush_customAlphaIx), AlphaPicker selection |

There is **one** `BrushRibbon_UI_Size` in the scene (registered as `instance` in Awake). The same component is referenced by both `SD_WorkflowOptionsRibbon_UI._brushSize_slider` and `BrushRibbon_UI._size` when the layout uses a single toolchest.

---

## 2. Flow: selecting a brush (glue attached)

When the user selects a brush in **BrushRibbon_UI_AlphaPicker**:

1. **BrushRibbon_UI_AlphaPicker.SelectBrushAtIndex(index)**
   - Sets `_brushAlphasMGR.CurrentIndex = index` → stamp and entry data follow.
   - Hardness UI: `_hardness.SetUsingCustomAlpha(index - 3)` or `SetBuiltInOnly(index)` so the hardness button reflects built-in vs custom.
   - Gets `suggestedSize01 = _brushAlphasMGR.GetSuggestedSize01(index)` and `suggestedSpacing01 = _brushAlphasMGR.GetSuggestedSpacing01(index)`.
   - Calls **ApplyBrushOptionsToRibbon(suggestedSize01, suggestedSpacing01)**:
     - Writes to **BrushRibbon_UI_Size.instance** (SetBrushSize when suggestion > 0, SetBrushSpacing always). Fallback to SD ribbon or BrushRibbon_UI if instance is null.
   - **UpdateSelectedBrushLabels(index)** sets name and "Size | Spacing" from entry + `BrushRibbon_UI_Size.GetBrushSize01()`.
   - **HighlightSelected(index)** updates grid highlight.

So: **selection → CurrentIndex + canonical size/spacing + labels**. No separate “apply” step; the glue is in SelectBrushAtIndex.

---

## 3. Flow: reading brush state (paint path)

| Consumer | Size | Spacing | Stamp | Notes |
|----------|------|---------|--------|------|
| **MaskPainter** | `BrushRibbon_UI_Size.GetBrushSize01()` | — | — | visibleBrushSize, CursorPreviewUI_Reposition, OnPointerDown brushSize, PaintOnTexture brushSize. Shift+RMB resize writes to `BrushRibbon_UI_Size.instance.SetBrushSize` (fallback SD). |
| **Cursor_UI.SetCursorThickness** | Called with `BrushRibbon_UI_Size.GetBrushSize01()` from MaskPainter | — | — | Cursor reflects canonical size. |
| **Inpaint_MaskPainter** | (base MaskPainter uses GetBrushSize01) | — | `BrushAlphas_MGR.GetCurrentBrushStampTex() ?? SD_WorkflowOptionsRibbon_UI.instance?._brushHardnessTex` | Stamp from canonical MGR first, then ribbon fallback. |
| **Projections_MaskPainter** | (base MaskPainter) | — | Same as Inpaint_MaskPainter | Same. |
| **ProjectorCameras_RenderHelper** | — | — | `BrushAlphas_MGR.GetCurrentBrushStampTex() ?? oRib._brushHardnessTex` | Preview cursor uses canonical stamp first. |
| **Save (BrushRibbon_UI_SL)** | `_size.Save(trSL)` → maskBrush_size01 | maskBrush_spacing01 | — | Size/spacing from the one BrushRibbon_UI_Size. |
| **Load** | `_size.Load(trSL)` | same | — | Restores size/spacing; Hardness Load restores CurrentIndex (custom alpha). |

So: **all paint and cursor code reads size from GetBrushSize01 and stamp from GetCurrentBrushStampTex (with ribbon fallback)**. No direct reads of SD_WorkflowOptionsRibbon_UI for size or stamp when the canonical source exists.

---

## 4. Remaining dependencies on SD_WorkflowOptionsRibbon_UI

These are **intentional** (not brush identity/size/stamp):

- **Opacity, color, add/erase, tablet pressure, depth limit**: MaskPainter and Inpaint_MaskPainter still use `SD_WorkflowOptionsRibbon_UI.instance` for maskBrushOpacity, brushColor, isPositive, tabletPressureMode, brushDepthLimit01. These are workflow options; the refactor did not move them to a second source of truth.
- **isDoingSomethingElse()**: Requires `SD_WorkflowOptionsRibbon_UI.instance != null` (and WorkflowRibbon_UI, MainViewport_UI) to allow painting. So painting is currently only allowed when the workflow ribbon is present. The Paint tab normally reparents that panel into the toolchest, so in normal use the instance is set. If you need painting when only a minimal Paint tab exists without the SD panel, that would require a separate change (e.g. fallback opacity/color and relaxed isDoingSomethingElse).

---

## 5. Save/Load consistency

- **Project save**: `SD_WorkflowOptionsRibbon_UI.instance.Save(spz)` (and/or BrushRibbon_UI when used) persists brush state. The saved `BrushRibbon_UI_SL` holds maskBrush_size01, maskBrush_spacing01, maskBrush_customAlphaIx, etc. Those are filled from the **same** BrushRibbon_UI_Size and BrushRibbon_UI_Hardness that reference BrushAlphas_MGR.
- **Project load**: Load restores into that same BrushRibbon_UI_Size (and Hardness restores CurrentIndex). So after load, GetBrushSize01(), GetBrushSpacing01(), and GetCurrentBrushStampTex() reflect the restored state.

---

## 6. Checklist: selection → settings applied and glued

- [x] Selecting a brush sets **BrushAlphas_MGR.CurrentIndex**.
- [x] Stamp used in paint is **BrushAlphas_MGR.GetCurrentBrushStampTex()** (with ribbon fallback).
- [x] Suggested size/spacing from ABR are written to **BrushRibbon_UI_Size.instance** in ApplyBrushOptionsToRibbon.
- [x] All size reads in the paint path use **BrushRibbon_UI_Size.GetBrushSize01()**.
- [x] Cursor thickness uses the same GetBrushSize01().
- [x] Save/Load persist and restore the same BrushRibbon_UI_Size and custom alpha index.
- [x] Labels (name, Size | Spacing) use canonical GetBrushSize01() and entry data.

---

## 7. Summary

The brush system now has **two canonical pillars**:

1. **BrushRibbon_UI_Size** – single source for brush size and spacing; all reads via GetBrushSize01() / GetBrushSpacing01(); all writes (selection, default, resize) go to `BrushRibbon_UI_Size.instance` when present.
2. **BrushAlphas_MGR** – single source for which brush is selected (CurrentIndex) and its stamp (CurrentBrushStampTex); app-wide stamp via GetCurrentBrushStampTex().

Selecting a brush in the Paint tab updates both pillars (index + suggested size/spacing), and the rest of the app reads from these two sources so the same settings stay in effect until the user changes them again. This is a **fundamental** change in how the source code handles brushes, not an ancillary layer.
