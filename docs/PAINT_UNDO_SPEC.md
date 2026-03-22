# Paint undo — product spec (MVP)

## Granularity

- **Stroke boundary:** One undo step per completed brush stroke (pointer up after `OnFinal_ApplyIncomingVals_intoMask` succeeds). Not per-frame or per-tick.

## Scope (MVP)

- **Active layer only:** Snapshots capture the **current paint target** (`GetPaintTarget()` → layer `Content` or `_ObjectUV_brushedColorRGBA` when no stack).
- **Lossless:** Raw RGBA per UDIM slice, compressed losslessly (Deflate) on CPU for history.

## Depth and session

- **Max depth:** Configurable in **Settings** (runtime rows) and via `Settings_MGR.get_paintUndo_maxDepth()` (default 8, clamped **1–`PAINT_UNDO_DEPTH_MAX` (16)** to limit RAM). Stored in PlayerPrefs. `StaticEvents`: `Settings:set_paintUndo_maxDepth` (int), `Settings:set_paintUndo_enabled` (bool).
- **Session-only:** Undo/redo stacks are **not** serialized with project save (cleared on load/new scene lifecycle as implemented in `PaintUndo_MGR`).

## Layer stack rules

- On **structural** stack changes (`PaintLayerStack_MGR.OnLayersChanged`: add/remove/move/collapse path that fires this event), **clear** undo and redo to avoid mismatched indices.
- If snapshot **metadata** (layer count, active index, dimensions) does not match the live stack at restore time, that operation is **skipped** (with log).

## Input

- **Ctrl+Z / Cmd+Z:** Undo  
- **Ctrl+Y / Cmd+Y** (and **Ctrl+Shift+Z / Cmd+Shift+Z**): Redo  
- Suppressed when `KeyMousePenInput.isSomeInputFieldActive()` is true.

## Painting during restore

- While a restore is **in progress** (`PaintUndo_MGR.BlocksNewStroke`), new strokes are blocked (`MaskPainter.isDoingSomethingElse()`).

## Related docs

- [`UNDO_INTEGRATION.md`](UNDO_INTEGRATION.md) — hooks and code paths  
- [`LAYER_SYSTEM_STROKE_STORAGE_ANALYSIS.md`](LAYER_SYSTEM_STROKE_STORAGE_ANALYSIS.md) — layer / `RenderUdims` model  
