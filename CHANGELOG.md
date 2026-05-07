# StableProjectorz — Changelog

**Unity Player Settings version:** `2.4.5`  
This file tracks changes on top of that baseline until the next release bump.

---

## [Unreleased] — 2026-04-25 — File browser input focus lock (viewport command isolation)

### Summary

When `SimpleFileBrowser` is open, viewport and workflow mouse commands no longer react to wheel/right-click input behind the dialog. Input gating is now tied to **file browser open state** (not generic global click locks) so normal 3D orbit/rotation behavior stays intact when the browser is closed.

### Input focus lock behavior

- **`Assets/_gm/_Core/IO/Keyboard Mouse Input/KeyMousePenInput.cs`:**
  - Added `isFileBrowserOpen()` (reflection to `SimpleFileBrowser.FileBrowser.IsOpen`).
  - Mouse button presses/releases and mouse deltas now return neutral values while file browser is open.
  - Scope is intentionally narrowed to file-browser-open only to avoid blocking unrelated interaction modes.

### Viewport / workflow scroll paths aligned to file browser lock

- **`Assets/_gm/Features/Camera/Navigation/CameraDolly.cs`:**
  - Mouse wheel zoom ignores scroll while file browser is open.
- **`Assets/_gm/Features/Camera/Navigation/Camera_UV_NavigateHelper.cs`:**
  - UV zoom wheel path ignores scroll while file browser is open.
- **`Assets/_gm/Features/Camera/Navigation/CameraOrbit_ClickPivot.cs`:**
  - Pivot recenter stop condition now treats wheel input as inactive while file browser is open.
- **`Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/WorkflowRibbon_UI.cs`:**
  - Shift/Ctrl + scroll workflow-mode switching is disabled while file browser is open.

### Notes for testers

- Open any file dialog and use mouse wheel/right-click: viewport camera/workflow should not respond until dialog closes.
- After closing dialog, confirm 3D orbit/rotation/pan/zoom resumes immediately.

---

## [Unreleased] — 2026-04-25 — SPZ GO bridge hardening, startup stability, WebUI GPU selection

### Summary

Stabilized Blender/SPZ GO exchange and in-app add-on behavior, reduced startup window flicker from addon server launch/restart paths, and improved Stable Diffusion GPU device selection so Restart WebUI respects the selected CUDA device more reliably.

### SPZ GO / Blender bridge

- **`External/Blender_SpzBridge/__init__.py`:**
  - `spz.go_export` / exchange FBX export now supports:
    - **selection present:** export active object (fallback first selected),
    - **no selection:** default full-scene FBX behavior (`use_selection=False`).
  - Added texture auto-assign after SPZ import:
    - finds best exchange texture beside exported FBX,
    - ensures node graph on target mesh material (`UV Map -> Image Texture -> Principled Base Color`),
    - applies to selected/imported mesh objects.
  - Added new operator/button: **`Apply SPZ maps only`** (`spz.go_apply_maps_only`) to refresh maps from SPZ and apply to selected meshes without reimporting geometry.

### SPZ GO (in-app add-on + RPC reliability)

- **`Assets/StreamingAssets/Addons/StableProjectorzGO/__init__.py`:**
  - Added detailed import/export diagnostics (raw/normalized paths, existence/size checks, RPC payload/response, exception traceback) to console for troubleshooting.
- **`Assets/_gm/Features/AddonSystem/AddonUI_MGR.cs`:**
  - SPZ GO native in-process import/export button path now **falls back to Python callback** automatically when native fast-path fails.
- **`Assets/_gm/Features/AddonSystem/Addon_SocketServer.cs`:**
  - Replaced single 5s RPC wait with method-aware timeouts:
    - default commands: 10s
    - long ops (import/export/save/load, texture export): 120s
- **`Assets/StreamingAssets/AddonSystem/spz.py`:**
  - Client method-aware timeouts aligned for long operations (120s).
- **`Assets/_gm/Features/3D Models/ModelsHandler3D_ImportHelper.cs`:**
  - `.fbx` export-to-path favors binary-capable scene re-export when live model exists;
  - improved fallback/logging when only cached bytes are available.

### Startup / launch behavior

- **`Assets/_gm/Features/AddonSystem/Addon_MGR.cs`:**
  - Add-on Python server now launches hidden in player builds (reduced visible console flicker).
  - Added `_autoRestartWithAddonsOnServerFail` (default `false`) so relaunch via `Run_with_Addons.bat` is opt-in, avoiding surprise restart/flicker.

### Stable Diffusion GPU selection

- **`Assets/_gm/Features/StableDiffusion/Webui/Launch_WebUI_bat_File.cs`:**
  - Hardened `GetLaunchPathWithGpuSetting`:
    - parses launch path + appended args robustly,
    - prefers direct `launch.py` execution whenever available,
    - uses venv python when present, else PATH python fallback,
    - preserves extra launch args in `COMMANDLINE_ARGS`,
    - keeps `CUDA_VISIBLE_DEVICES` flow and adds clearer logs for selected path/fallback.

### Notes for testers

- SPZ GO in-app Import/Export: check Unity console for detailed diagnostics if failure occurs.
- Blender SPZ GO: test both **Import from StableProjectorz** and **Apply SPZ maps only** on selected mesh.
- GPU switching: change Settings `SD GPU`, click **Restart WebUI (apply GPU)**, verify `[LaunchWebUI]` logs show direct launch path and selected CUDA device.

---

## [Unreleased] — 2026-04-07 — Add-on API: editor layout, display mode, UI chrome (refactor notes)

### Summary

Expanded **JSON-RPC `spz.cmd.*`** (TCP **5555**) and the mirrored **FastAPI** routes / shipped **`spz.py`** so Python (and HTTP) add-ons can drive main-window chrome: skeleton column widths, OS/window display mode, ribbon tabs, cursor lock, global UI scale, named `GameObject` visibility, viewport status line, and `EventSystem` toggling. **`get_api_capabilities`** reports **`addon_rpc_version` `1.6`** — clients should not assume the old list or version.

**If you refactor:** keep **`Addon_SocketServer.BuildAddonApiCapabilities()`** in sync with **`ExecuteFastPathCommand`** early handlers (`TryExecuteEditorLayoutCommand`, `TryExecutePlayerDisplayCommand`, `TryExecuteUiChromeCommand`). Missing entries break discovery; missing `switch` cases break runtime calls. **`ExecuteCommand`** now does **`@params ??= new JObject()`** so omitting `params` never null-derefs handlers.

### New / changed C# (Unity)

| Area | Files | Notes |
|------|--------|--------|
| Editor columns | `Global_Skeleton_UI.cs` | Snapshots left/right `LayoutElement` widths; `SetSidePanelVisibility` zeroes or restores; `TryGetSidePanelVisibility` gates **get/set** RPC parity. |
| Socket routing | `Addon_SocketServer.cs` | Early RPCs **before** `FastPath_API` ready (except `get_addon_context`). UI chrome, display, editor layout grouped in static try-methods. |
| Display | `SpzPlayerDisplay_API.cs` | Windowed / borderless / exclusive fullscreen; batch-mode no-op on set; optional resolution + `refresh_rate_hz`. |
| Cursor | `SpzCursor_API.cs` | `None` / `Locked` / `Confined`; **`set_cursor_state`** treats JSON **`null`** keys as omitted (`TryGetValue` + `JTokenType.Null`). |
| Ribbon | `TabsGroup_UI.cs`, `CommandRibbon_UI.cs` | `TrySwitchTab`, `GetTabTitles`; `TrySwitchRibbonTabByTitle`, `GetRibbonTabTitles`. |
| UI scale & targets | `SpzUiChromeOps.cs`, `SpzUiChromeRegistry.cs` | Main scaler = **`Global_Skeleton_UI`** + **`CanvasScaler`** (**Scale With Screen Size** only). **`_referenceBaseline`** captured **once per session** (do not overwrite on scaler refresh). Built-in target ids resolved before registry extras. |
| Registry (optional extras) | `SpzUiChromeRegistry.cs` | **Added to** `UI_Global_Skeleton.unity` on skeleton canvas root; inspector **`_extraTargets`** for more named ids. |

### New `spz.cmd.*` surface (non-exhaustive; see capabilities)

- **Layout:** `get_editor_layout`, `set_editor_layout` (`mode`, `left_visible`, `right_visible`).
- **Display:** `get_display_mode`, `set_display_mode` (`mode`, optional `width` / `height` / `refresh_rate_hz`; validation + partial-dimension normalize in `SpzPlayerDisplay_API`).
- **Ribbon / cursor:** `get_ribbon_tabs`, `set_ribbon_tab`, `get_cursor_state`, `set_cursor_state`.
- **UI chrome:** `get_ui_scale`, `set_ui_scale`, `list_ui_targets`, `get_ui_target_active`, `set_ui_target_active`, `show_status_text`, `get_event_system`, `set_event_system`.

### Shipped Python / HTTP (`Assets/StreamingAssets/AddonSystem/`)

- **`spz.py`:** `api.editor`, `api.display`, `api.ui_chrome` (+ `editor()`, `display()`, `ui_chrome()` helpers).
- **`http_server.py`:** `/api/v1/editor/layout`, `/api/v1/display/mode`, `/api/v1/chrome/*` (ribbon, cursor, ui-scale, ui-targets, status-text, event-system).

### Risk / testing flags for refactors

- **UI scale:** Wrong baseline logic breaks multiplier; scaler must stay **Scale With Screen Size** on skeleton for set/get to succeed.
- **Hiding targets:** `global_skeleton_canvas` / disabling **EventSystem** can brick input — API allows it on purpose.
- **IL2CPP / headless:** Display and some UI paths may no-op or return errors; batch mode guarded where noted.

---

## [Unreleased] — 2026-04-05 — Command ribbon tab strip (many add-ons, resize)

### Summary

The right-panel command ribbon tab row adapts when many add-on tabs load or when the panel width changes, instead of breaking layout by forcing equal flex widths against per-tab `LayoutElement.minWidth`.

### `CommandRibbon_UI.cs`

- **`PatchTabStripResponsiveLayout`:** Sets `HorizontalLayoutGroup.childForceExpandWidth = false` (keeps `childControlWidth = true`) so label-driven minimum widths work with the layout group until rebalanced.
- **`RebalanceStripTabMinWidthsIfOverflowing`:** If the sum of tab `LayoutElement.minWidth` exceeds the strip budget (after padding and spacing), scales mins down proportionally with crowded floors (**36px**, then **28px**), then an iterative proportional pass down to **1px** when still over budget (guarded iteration + no-progress exit).
- **`HarmonizeStripTabTypography`:** After `ApplyStripTabMinWidthForLabel` for each direct strip tab, calls rebalance so overflow is handled whenever strip typography is applied.
- **Resize:** `Update` polls effective tab strip `RectTransform.rect.width`; when it changes by more than **1px**, calls **`RefreshTabStripLayout`**. If the strip is missing, **`_lastRibbonStripWidth`** resets so the next valid strip re-initializes tracking.
- **`FindFirstOtherStripTabForStyleReference`:** Only uses **`TabsGroupElem_UI`** instances that are **direct children** of the strip (aligned with harmonize / rebalance).
- **Deferred rebuild:** **`QueueTabStripRebuildNextFrame`** replaces overlapping **`StartCoroutine(RebuildTabStripLayoutNextFrame)`** calls; **`_rebuildTabStripLayoutSeq`** ensures `finally` only clears **`_rebuildTabStripLayout_crtn`** when that run is still the latest (avoids a stopped coroutine wiping the handle for a newer run).
- **`EnsurePaintTabExists`:** Uses **`QueueTabStripRebuildNextFrame`** for the same coalesced next-frame path as add-on refresh.

### Notes for testers

- With many add-ons and long titles, narrow and widen the right panel; tabs should stay in a single row with ellipsis where needed, without layout explosion.
- If the tab row sits in a horizontal **ScrollRect**, the next-frame pass still walks parents and scrolls right after new tabs (unchanged intent).

---

## [Unreleased] — 2026-03-25 — Smudge brush tool

### Summary

Adds a smudge brush as a third tool mode alongside paint and erase in the inpaint brush direction strip. Smudging blends neighboring pixel colors under the brush in real time during the stroke.

### New files

- **`BrushToolMode.cs`** — `Paint`, `Smudge`, `Erase` enum used by the direction toggle strip and paint pipeline.

### Brush direction UI (`SD_BrushRibbon_UI_Direction.cs`, `BrushRibbon_UI_Direction.cs`)

- `BrushRibbon_UI_Direction` gains `isSmudge`, `toolMode`, `SetToolMode(BrushToolMode)`.
- `SD_BrushRibbon_UI_Direction.CreateSmudgeToggle_IfNeeded()` programmatically clones the brush toggle, inserts it between paint and erase, re-anchors all three to vertical thirds, loads `icon_smudge` sprite via Resources / editor asset database.
- Cursor color set to gray when smudge is active.

### Smudge compute shader (`ApplyBrushStroke_IntoMask.compute`)

- New `CSSmudge` kernel: 5×5 weighted-average blur with spacing proportional to brush size, applied only where the R8 brush stroke has coverage. Reads from `_SmudgeSourceCopy` (snapshot), writes to `_PaintedMask`.
- Reuses `apply_brush_stroke_smudge()` from `Brush_ApplyFinalStroke_ToMask.cginc`.

### C# wiring

- **`ApplyBrushStroke_ToUvMask.Apply_smudge_to_ColorBrushTex()`** — copies paint target to temp RT, dispatches `CSSmudge` with `BLEND_RGBA_ONCE + SMUDGE_MODE` keywords. Manages persistent `_smudgeCopyRT`.
- **`Inpaint_MaskPainter.OnRenderIntoCurrTex_please()`** — when smudge is active, dispatches smudge compute each frame during drag (captures undo on first frame).
- **`Inpaint_MaskPainter.OnFinal_ApplyIncomingVals_intoMask()`** — skips normal color-apply when smudge (already applied per frame); fires stroke-end event and re-render.
- **`Inpaint_MaskPainter.getBrushStrength()`** — returns positive opacity when smudge (no sign flip).
- **`SD_WorkflowOptionsRibbon_UI`** — exposes `isSmudge` and `brushToolMode` from direction component.

### Notes for testers

- Smudge only affects inpaint color painting (not projection masks or backgrounds).
- If the smudge icon does not appear, assign `Assets/_gm/Art/Icons/icon_smudge.png` as a sprite in a Resources folder or via the serialized field on the prefab.
- Undo captures the paint target on the first frame of the smudge stroke.

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

### Paint undo — Thompson sampling, contextual arms, capture bandit, scratch split (2026-03-24)

**`PaintUndo_Scheduler.cs`**

- **`RestoreBudgetPolicy`:** `FixedMiddle`, `Ucb1`, `Thompson` (default wired from `PaintUndo_MGR` inspector).
- **Restore (Thompson):** Beta–Bernoulli per budget arm, **per context bucket** (`QuantizeContextBucket`); **`restorePosteriorDecayPerSession`** blends posteriors toward uniform on each `BeginRestoreSession`.
- **`RegisterRestoreBanditObservation(hitchMs, uploaded)`:** UCB keeps continuous reward; Thompson uses binarized success vs **`restoreThompsonSuccessHitchMs`**. **`RegisterUcbReward`** retained as obsolete for UCB-only.
- **Capture bandit:** Discrete **arms** for readback inflight + post-readback yields; **cold-start** uses legacy **`GetCaptureGpuReadbackMaxInflight` / `GetCapturePostReadbackYieldFrames`** for the first N jobs per bucket; post-capture **hitch window** feeds **`RegisterCaptureBanditObservation`**.

**`PaintUndo_MGR.cs`**

- **Separate scratch buffers:** **`_scratchRestore`** for undo/redo readback; **`_scratchCapture[2]`** ping-pong for capture (optional **`_capturePingPongScratches`**); optional **`_captureEagerGpuCopy`** for single-queued-job early `CopyTexture`.
- Inspector toggles for restore policy, capture bandit, min pulls per bucket, ping-pong, eager copy.
- **`LateUpdate`:** calls **`RegisterRestoreBanditObservation`** (not UCB-only API).

### UI — `TMP_InputField` undo / redo (2026-03-24)

**`Assets/_gm/_Core/UI (reusable)/UiTextUndo/`**

- **`UiTextUndo_MGR` + `UiTextUndo_Input`:** After scene load bootstrap (`RuntimeInitializeOnLoadMethod`); registers **`TMP_InputField`** instances (rescans periodically) with per-field undo/redo stacks (max depth configurable).
- **Ctrl/Cmd+Z** (undo) and **Ctrl/Cmd+Y** or **Ctrl/Cmd+Shift+Z** (redo) while a TMP field is focused (`DefaultExecutionOrder(-40)`); **`PaintUndo_Input`** still skips the viewport stack when a field is active — no conflict.

### Command ribbon — Paint tab visual parity (2026-03-24)

**`CommandRibbon_UI.cs` (`EnsurePaintTabExists` / `ApplyPaintTabStripVisuals`)**

- Prefab ribbon tabs show the **flared sliced sprite only under `go active`** (hidden when deselected). The runtime Paint tab had **`TabBg`** using the same slice **always**, so it looked permanently selected.
- **`TabBg`** is now an **invisible** full-rect hit target; **sprite / sliced styling apply only to the active-state `Image`**. Tuning copy from reference tabs targets the **active** graphic only (`CopySlicedTabImageTuningFromReference`).
