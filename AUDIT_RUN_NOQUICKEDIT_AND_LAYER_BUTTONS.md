# Audit: run_noQuickEdit + Layer Buttons (Primary, Secondary, Tertiary)

## 1. run_noQuickEdit / WebUI launch

### Primary
- **Launcher location**: `LaunchWebUIBatFile` lives in **Managers_Global.unity** on GameObject "LAUNCHES WEB UI BAT FILE" (active, script enabled). Managers_Global loads in the **first** batch in `Start_Scene_Global_MGR`, so it runs early.
- **Start()**: In build, `Start()` calls `LaunchWebui_Manually()`. In editor it returns immediately (`#if UNITY_EDITOR return`).
- **GetWebuiFilePath()**: Tries env `SPZ_WEBUI_RUN_PATH`, then paths under `Directory.GetParent(Application.dataPath)` (exe dir in build), then parent dirs. Returns "" if not found and logs; status text shown if `printStatusText_ifNotFound`.

### Secondary
- **.lnk + keepWindow**: For `.lnk` we previously always used `/C start "" "path"`, so the CMD window closed immediately. **Fix**: When `keepWindow` is true, use `/K` so the CMD window stays open and the user sees it (and `start` still opens the .lnk in another window).
- **Single attempt**: If the path wasn’t ready (e.g. slow disk) or launch failed, we never retried. **Fix**: Added `RetryLaunchOnceAfterDelay` (1.5s) so we try again once if `_lastLaunchedWebUiPid` is still 0.

### Tertiary / Cross-correlation
- Restart/Settings use `LaunchWebui_Manually` / `GetLaunchPathWithGpuSetting`; no change needed.
- `StartExternalProcess`: `attachToConsole: false` and `keepWindow: true` / `hidden: false` already used for WebUI launch; no further change.

---

## 2. Layer buttons (Select / Delete / Visibility)

### Primary
- **Dispatcher**: `LayerRowClickDispatcher` on each row does hit-testing and calls `TryRemoveLayer`, `SetActiveLayer`, or `SetLayerVisible`. It was only `IPointerClickHandler`; sometimes ScrollRect/parent consumes the click. **Fix**: Also implement `IPointerDownHandler` and run the same `DispatchClick()` for both so the action runs even if the click never fires.
- **Row raycast**: If the row had no `Image` (e.g. template), we never set `raycastTarget = true`, so the EventSystem never targeted the row. **Fix**: If the row has no `Image`, add one (near-transparent) and always set `raycastTarget = true` on the row.

### Secondary
- **Find only direct children**: `Find("SelectLayer")` / `Find("Delete")` / `Find("Visibility")` only search direct children; template rows with nested hierarchy (e.g. row → Panel → SelectLayer) got null rects and the dispatcher did nothing. **Fix**: Added `FindChildRecursive(root, childName)` and use it for Select, Delete, and Visibility so rects are always set when the controls exist.
- **Toggle without visibilityRect**: When the row used a `Toggle` instead of a "Visibility" button, we never set `visibilityRect`, so the dispatcher couldn’t hit-test visibility. **Fix**: Set `visibilityRect = visibilityToggle.transform as RectTransform` in the Toggle branch.

### Tertiary
- **Click on row (e.g. name)**: Clicks on the row that didn’t hit any button did nothing. **Fix**: At the end of `DispatchClick()`, if no button was hit, call `stack.SetActiveLayer(layerIndex)` so clicking the row selects the layer.
- **Stack methods**: `SetActiveLayer`, `SetLayerVisible`, `RemoveLayer` (and `TryRemoveLayer` cooldown) are implemented in `PaintLayerStack_MGR` / `PaintTab_LayersPanel_UI`; no scaffolding.

---

## 3. Files changed

| File | Changes |
|------|--------|
| `StartExternalProcess.cs` | For `.lnk`, use `keepWindow` to choose `/K` vs `/C` so CMD can stay open. |
| `Launch_WebUI_bat_File.cs` | Added `RetryLaunchOnceAfterDelay(1.5s)` so WebUI launch is retried once if the first attempt didn’t set a PID. |
| `PaintTab_LayersPanel_UI.cs` | `FindChildRecursive` for Select/Delete/Visibility; ensure row has Image + `raycastTarget = true`; set `visibilityRect` for Toggle; dispatcher implements `IPointerDownHandler` and shares `DispatchClick()`; fallback “click on row” → select layer. |

---

## 4. If it still doesn’t work

**run_noQuickEdit**
- Confirm build (not editor) so `Start()` doesn’t early-return.
- Check player log for "Webui file found" or "Webui file not found" and for "Attempting to execute: ..." / "Process started successfully" or CreateProcess errors.
- Ensure `stable-diffusion-webui-forge/run_noQuickEdit.bat` (or `.lnk`) exists next to the exe or set `SPZ_WEBUI_RUN_PATH`.

**Layer buttons**
- Confirm the Paint tab is open and the layers panel is built (e.g. `PaintTab_CollectPaintUI.CollectNow()` ran).
- Check for a CanvasGroup with `blocksRaycasts = true` or another full-panel blocker in front of the list.
- Ensure an EventSystem exists in the scene.
