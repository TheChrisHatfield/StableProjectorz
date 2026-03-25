# Paint undo — integration map

Living document for `PaintUndo_*` and core touch points. **Do not** put implementation notes in Context_Ref; update this file when hooks change.

## Hook points

| Location | When | Purpose |
|----------|------|---------|
| [`Inpaint_MaskPainter.OnFinal_ApplyIncomingVals_intoMask`](../Assets/_gm/Features/Paint/Inpaint/Inpaint_MaskPainter.cs) | Before `Apply_into_ColorBrushTex` | Schedules **pre-stroke** capture; non-stack tag `PaintUndoNonStackTarget.InpaintColor` (default overload). |
| [`Background_Painter.OnFinal_ApplyIncomingVals_intoMask`](../Assets/_gm/Features/Paint/BG%20painter/Background_Painter.cs) | Before final blit into background mask | `SchedulePreStrokeCapture(mask, PaintUndoNonStackTarget.BackgroundGenMask)`. |
| [`Projections_MaskPainter.OnRenderIntoCurrTex_please`](../Assets/_gm/Features/Paint/ProjectionsMasking/Projections_MaskPainter.cs) | First frame of stroke only, **single-POV** (`numPOV == 1`), before `Apply_into_MaskUtils` | `SchedulePreStrokeCapture(uvMask, PaintUndoNonStackTarget.ProjectionGenMask)`. Multi-POV: no capture (undo not wired for that path yet). |
| [`MaskPainter.isDoingSomethingElse`](../Assets/_gm/Features/Paint/MaskPainter.cs) | Every paint/drag gate | Blocks strokes while `PaintUndo_MGR.BlocksNewStroke` (inpaint, background, and projection painters). |
| [`PaintLayerStack_MGR.OnLayerStackStructureChanged`](../Assets/_gm/Features/Paint/Layers/PaintLayerStack_MGR.cs) | Add/remove/reorder, load, resolution/UDIM change | `PaintUndo_MGR` clears history (not on visibility/opacity — those use `OnLayersChanged` for UI only) |
| [`PaintUndo_Input`](../Assets/_gm/Features/Paint/Undo/PaintUndo_Input.cs) | `Update` | Ctrl/Cmd+Z / redo shortcuts (not Unity **Ctrl+B** Build) |
| [`Inpaint_MaskPainter.Awake`](../Assets/_gm/Features/Paint/Inpaint/Inpaint_MaskPainter.cs) | Startup | `PaintUndo_MGR.EnsureExists()` |

**Capture ordering (correctness):** Immediately before `Apply_into_ColorBrushTex`, `PaintUndo_MGR` copies **target → scratch** (`Graphics.CopyTexture`). That snapshot is the **pre-stroke** state. After the compute apply, the scratch still holds pre-stroke texels; async readback compresses into the undo ring.

**Capture throttling (heavy UV / 4K):** `PaintUndo_Scheduler.EvaluateWorkload` + `GetCaptureGpuReadbackMaxInflight` choose **staggered** `AsyncGPUReadback` (sliding window via `TextureTools_SPZ.RenderTexture_to_Texture2DList_Async_Staggered`) when complexity is high; light loads keep **all slices in parallel**. `GetCapturePostReadbackYieldFrames` adds 0–3 frame yields before CPU blob pack to spread main-thread work.

**Deferred shortcuts:** `PaintUndo_MGR.TryUndo` / `TryRedo` used to no-op while `IsBusy` (capture coroutine running). Ctrl+Z during GPU readback/deflate is now **queued** (capped) and `ProcessDeferredUndoRedo` runs when capture finishes or after each restore—same global `PaintUndo_MGR` instance (`DontDestroyOnLoad`).

**Global stroke order (all layers):** The undo list is **one chronological stack** for the session. Each entry stores the **actual** paint buffer: `SchedulePreStrokeCapture` uses `PaintLayerStack_MGR.IndexOfContent(paintTarget)` so metadata matches the layer whose `Content` was written, not only the active index. If the stroke went to the standalone `Inpaint_MaskPainter._ObjectUV_brushedColorRGBA` (layer `Content` not used), `LayerCount` is stored as **0** and restore uses `PaintUndo_SnapshotRecord.NonStackTargetKind` + `TryResolveNonStackRestoreTarget` — **not** guessing from the active layer.

**Non-layer targets (`LayerCount` ≤ 0):** `PaintUndoNonStackTarget` distinguishes inpaint color vs background-gen mask vs single-POV projection mask so restore uploads to the correct `RenderUdims` (dimensions + `GraphicsFormat` must match the snapshot).

## Restore order (GPU + SD)

1. Decompress snapshot to per-slice CPU buffers (may be amortized across frames).
2. Upload slices into the **`Content` of the layer recorded in the snapshot** (`ActiveLayerIndex`), not necessarily the layer currently selected in the UI (so undo/redo still applies after switching layers).
3. `PaintLayer.SyncDataFromContent()` on whichever layer owns that `Content`.
4. `Objects_Renderer_MGR.ReRenderAll_soon()`.
5. `Objects_Renderer_MGR.EnsureInpaintColorLayerAppliedForCapture()` so img2img/capture sees updated paint.

If any step is skipped, SD or viewport can disagree with layer texels.

**Workload-aware scheduling:** `PaintUndo_Scheduler.BeginRestoreSession(width, height, sliceCount)` derives a complexity score from total pixels (resolution × UDIM count). Heavy targets (e.g. 4K, many tiles) automatically use fewer slices per frame, higher per-frame time caps, faster EWMA on hitches, and a fresh UCB1 pass per restore so the bandit explores for that job—not a one-size-fits-all budget.

## Invariants

- `PaintUndo_MGR` is a **facade**; painters call **only** `ScheduleSnapshotAfterCopyPreStroke` / public API — not `Storage` directly.
- **Scratch `RenderUdims`** must match target width/height/slice count/format or it is reallocated.
- **Redo** capture uses the same scratch; no overlapping async readbacks (serialized by `_readbackBusy`).

## Observability

Filter logs by prefix **`[PaintUndo]`**.

## Profiling

See [`PAINT_UNDO_PROFILING.md`](PAINT_UNDO_PROFILING.md) for suggested targets and tunables.
