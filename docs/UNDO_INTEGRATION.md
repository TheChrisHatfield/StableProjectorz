# Paint undo — integration map

Living document for `PaintUndo_*` and core touch points. **Do not** put implementation notes in Context_Ref; update this file when hooks change.

## Hook points

| Location | When | Purpose |
|----------|------|---------|
| [`Inpaint_MaskPainter.OnFinal_ApplyIncomingVals_intoMask`](../Assets/_gm/Features/Paint/Inpaint/Inpaint_MaskPainter.cs) | After `Apply_into_ColorBrushTex`, before/after ordering: see below | Schedules **pre-stroke** GPU capture via `PaintUndo_MGR` (CopyTexture → async readback → push undo) |
| [`MaskPainter.isDoingSomethingElse`](../Assets/_gm/Features/Paint/MaskPainter.cs) | Every paint/drag gate | Blocks strokes while `PaintUndo_MGR.BlocksNewStroke` |
| [`PaintLayerStack_MGR.OnLayersChanged`](../Assets/_gm/Features/Paint/Layers/PaintLayerStack_MGR.cs) | Stack structure changes | `PaintUndo_MGR` clears history |
| [`PaintUndo_Input`](../Assets/_gm/Features/Paint/Undo/PaintUndo_Input.cs) | `Update` | Ctrl/Cmd+Z / redo shortcuts |
| [`Inpaint_MaskPainter.Awake`](../Assets/_gm/Features/Paint/Inpaint/Inpaint_MaskPainter.cs) | Startup | `PaintUndo_MGR.EnsureExists()` |

**Capture ordering (correctness):** Immediately before `Apply_into_ColorBrushTex`, `PaintUndo_MGR` copies **target → scratch** (`Graphics.CopyTexture`). That snapshot is the **pre-stroke** state. After the compute apply, the scratch still holds pre-stroke texels; async readback compresses into the undo ring.

## Restore order (GPU + SD)

1. Decompress snapshot to per-slice CPU buffers (may be amortized across frames).
2. Upload slices into **active layer `Content`** (or single-buffer target).
3. `PaintLayer.SyncDataFromContent()` on that layer.
4. `Objects_Renderer_MGR.ReRenderAll_soon()`.
5. `Objects_Renderer_MGR.EnsureInpaintColorLayerAppliedForCapture()` so img2img/capture sees updated paint.

If any step is skipped, SD or viewport can disagree with layer texels.

## Invariants

- `PaintUndo_MGR` is a **facade**; painters call **only** `ScheduleSnapshotAfterCopyPreStroke` / public API — not `Storage` directly.
- **Scratch `RenderUdims`** must match target width/height/slice count/format or it is reallocated.
- **Redo** capture uses the same scratch; no overlapping async readbacks (serialized by `_readbackBusy`).

## Observability

Filter logs by prefix **`[PaintUndo]`**.

## Profiling

See [`PAINT_UNDO_PROFILING.md`](PAINT_UNDO_PROFILING.md) for suggested targets and tunables.
