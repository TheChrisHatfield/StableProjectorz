# StableProjectorz — Changelog

**Unity Player Settings version:** `2.4.5`  
This file tracks changes on top of that baseline until the next release bump.

---

## [Unreleased] — 2026-03-11 — Paint layer stack / inpaint → mesh / SD bridge

### Summary

Multi-layer inpaint display and Stable Diffusion capture now follow the same final blit path as the proven single-layer case, with clearer fallbacks and resolution alignment to the mesh accumulation texture.

### Inpaint / layer display (`Inpaint_MaskPainter.cs`)

- **ApplyColorLayer_To_UV_Textures:** unified flow — resolve a single `source` `RenderUdims`, then one EntireColorLayer blit to accumulation (same pattern as `Count == 1` using `ActiveLayer.Content`).
- **CompositeVisibleLayersIntoTemp:** builds the multi-layer `source` by blitting each visible layer onto a cleared temp with `EntireColorLayer_BlitApply` (same material as single-layer), bottom→top, per-layer opacity; active layer can use `APPLY_LATEST_BRUSH_TOO` while painting.
- **Removed** dependency on `PaintLayerStack_MGR.CompositeTo` for the **mesh display** path (avoids runtime-created stack manager often having no compositor shader assigned; `CompositeTo` / `PaintLayer_CompositeBlend` remains for other uses).
- **GetLayerCompositeOrFallback:** multi-layer path uses `CompositeVisibleLayersIntoTemp` for consistency with viewport/SD mask source; fallbacks to active then layer 0.
- **maskResolution():** prefers accumulation texture width/height when UDIM slice count matches; if accumulation not ready, uses `SceneResolution_MGR.resultTexQuality` so layer RTs align with scene UV quality (reduces multi-blit / scale issues).
- **Earlier in same effort:** removed Inpaint_NoColor + SD-prep early return that skipped layer blit during capture; `EnsureBottomLayerHasSceneForComposite` + scene buffer setup for multi-layer; warnings when composite temp or visible `Content` missing.

### Layer stack manager (`PaintLayerStack_MGR.cs`)

- **CanCompositeLayers** public property (`_compositeBlendMat != null`).
- **CompositeTo:** if stack `_resolution` unset, adopt from first visible layer before allocating ping-pong temps; if temps still null, clear dest and log instead of leaving stale mask data.

### Renderer (`Objects_Renderer_MGR.cs`)

- **Apply_InpaintSketch_ColorLayer:** when layer count > 1, always run `ApplyColorLayer_To_UV_Textures` (UsualView still required); single-layer still gated on `allowed_to_showBrushMask()` so inpaint ribbon modes behave as before.

### Notes for testers

- After resolution changes, click in viewport or reload model once so brush `InitTextures` / `EnsureResolution` refresh layer buffers if sizes changed.
- If `PaintLayer_CompositeBlend` fails to load, mesh display no longer depends on it; collapse / mask helpers that still call `CompositeTo` may log warnings.

### Documentation / continual learning

- **`Context_Ref/REVIEW_Codebase_Summary.md`** (sibling folder under `Stable_Projectoz_Dev_Build`) — §4–5, §9–10 updated to match this paint/layer pipeline.
- **`Context_Ref/CONTINUAL_LEARNING.md`** — pointer to `continual-learning/` in this repo.
- **`continual-learning/paint-layers-sd-bridge.md`** — agent-oriented patterns (`→ Pattern:`) for layer/display/SD issues (folder is gitignored).
- **`continual-learning/build-stability.md`** — stub for batch/build patterns per `.cursor` rule.

### Multi-layer paint → SD capture (2026-03-21)

- **`EntireColorLayer_BlitApply.shader`:** `Blend One OneMinusSrcAlpha, One One` — RGB stays premultiplied-over; **alpha is additive** so stacking layers in `CompositeVisibleLayersIntoTemp` matches `PaintLayer_CompositeBlend` (fixes under-opaque composite bleeding accumulation into the content camera / SD init image).
- **`Inpaint_MaskPainter.ApplyColorLayer_To_UV_Textures`:** `forStableDiffusionCapture` bypasses the save-in-progress early return so generation does not capture paint-free accumulation.
- **`Objects_Renderer_MGR.EnsureInpaintColorLayerAppliedForCapture`:** passes `forStableDiffusionCapture: true` and calls **`GL.Flush()`** after refreshing the final material so GPU blits finish before capture.
- **`SD_GenRequests_Helper`:** `EnsureInpaintColorLayerAppliedForCapture` + **`WaitForEndOfFrame`** before img2img payload (non-background); same ordering when upscaling from live view (`fromGen_canBeNull == null`).
- **`SD_Generate_PayloadMaker`:** `EnsureInpaintColorLayerAppliedForCapture` before reading content/accumulation for payloads where applicable.

**Unity package manifests:** `Packages/manifest.json` and `Packages/packages-lock.json` were verified with no working-tree changes at this update (dependencies already in sync with `main`).

### Layer rename UI & enumerated collapse names (2026-03-22)

**Paint tab — layer list (`PaintTab_LayersPanel_UI.cs`)**

- Default names still come from the stack (`Layer 1`, `Layer 2`, …); **DisplayBlock** shows a read-only label (legacy-style).
- **Click the name** → **EditBlock** with `TMP_InputField`; **Enter** (`onSubmit`) commits via `SetLayerName`; **Escape** or **focus loss** (`onEndEdit`) cancels without saving.
- **Visibility** button also calls `SetActiveLayer` so the eye can select the active layer when the name strip is not used for selection.
- **OnActiveLayerChanged** only runs **`RefreshActiveHighlight`** (row tint), not a full list rebuild, so the rename field is not destroyed when the active layer changes.
- **Unity 6 TMP:** `SelectAll()` is protected — use **`onFocusSelectAll = true`** on the rename field instead of calling `SelectAll()`.

**Layer stack (`PaintLayerStack_MGR.cs`)**

- **`SetLayerName`**, **`DefaultLayerDisplayName`** — trim, max length, empty → default label; fires `OnLayersChanged` when the stored name changes.
- **`ConsumeNextDefaultCollapseLayerName`** — default merged-layer names **`Collapse 1`**, **`Collapse 2`**, … (monotonic counter).
- **`CollapseVisibleLayersIntoOne`** uses that API instead of a fixed `"Collapsed"` string.

**Save / load (`SerializationObjects.cs` + stack `Save`/`Load`)**

- **`PaintLayerStack_SL.nextCollapseNumber`** persists the collapse counter; **`0`** in older saves triggers **`InferNextCollapseNumber`** (parses `Collapse N` and legacy **`Collapsed`**).

**Inpaint (`Inpaint_MaskPainter.cs`)**

- **`CollapseLayersIntoScene`** renames the single result layer with **`ConsumeNextDefaultCollapseLayerName`** so it stays in sync with the paint-tab collapse counter.

### Paint undo / redo (session) + Settings UI (2026-03-22)

**New module `Assets/_gm/Features/Paint/Undo/`**

- **`PaintUndo_MGR`:** Pre-stroke snapshot (GPU copy → async readback → pack → **Deflate**); undo/redo restores via **inflated** slice buffers and **amortized** per-UDIM GPU upload in `LateUpdate` with **`PaintUndo_Scheduler`** (EWMA hitch proxy, LAVD-style aging, optional UCB1 budget arms).
- **`PaintUndo_Input`:** Ctrl+Z / Ctrl+Y (Cmd+Z on Mac) with input-field guard.
- **`PaintUndo_Storage`:** Ring buffer; max depth from settings (cap **16**).
- **`PaintUndo_SnapshotRecord` / `PaintUndo_Compress`:** Lossless wire format + zlib Deflate; **`TryBuildUncompressedBlob`** + **off-thread** Deflate on capture (and redo-stack snapshot) to reduce main-thread hitches.
- **Upload order:** **`LinearSliceUploadOrder`** (slice index 0…n−1; replaces misleading `VisibleFirstOrder` name).

**Hooks / safety**

- **`Inpaint_MaskPainter`:** `SchedulePreStrokeCapture` before `Apply_into_ColorBrushTex`; `PaintUndo_MGR.EnsureExists()` in `Awake`; **`GetPaintTarget_Undo()`** for restore target.
- **`MaskPainter`:** Blocks inpaint strokes while **`PaintUndo_MGR.BlocksNewStroke`** (restore in progress).

**Texture readback (`TextureTools_SPZ.cs`)**

- **`RenderTexture_to_Texture2DList_Async`:** Callback when **all** slice requests have **completed** (including error paths) via a response counter — avoids never-firing callback when any slice fails.

**Settings (`Settings_MGR.cs` / `Settings_UI.cs`)**

- **PlayerPrefs + UI:** `paintUndo_enabled`, `paintUndo_maxDepth` (clamped **1–16**); restore defaults resets them; **`SyncPaintUndoOnOffLabel`** keeps the **ON/OFF** text in sync when the toggle is set from code.
- **Runtime settings rows:** Paint undo enable row uses **Toggle + `Image` on the same GameObject** (reliable clicks), **bright green** selected state, **✓** checkmark, **ON/OFF** label; max-depth integer field row.
- **`_paintUndoSettingsRowsCreated`** is set **only after** rows build successfully (retries if panel/content was null).
- **VSync runtime row:** Larger hit target (112×28), checkmark, **`navigation = None`**, descriptive label **`raycastTarget = false`**.
- **SD GPU + paint-undo depth rows:** Descriptive labels **`raycastTarget = false`** so they do not steal clicks in the scroll view.
- **Shared helper:** **`AddToggleCheckmarkGraphic`** for runtime toggles.

**Documentation**

- **`docs/PAINT_UNDO_SPEC.md`**, **`docs/UNDO_INTEGRATION.md`**, **`docs/PAINT_UNDO_PROFILING.md`** — spec, integration, profiling notes.
