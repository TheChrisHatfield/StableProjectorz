# Layer panel buttons – line-by-line audit

## Why buttons can fail to work

1. **Row or parent steals the click** – Row Image or row Button with `raycastTarget = true` gets the event before child buttons.
2. **ScrollRect** – ScrollRect can treat the first pointer down as the start of a drag and not forward a click to the button.
3. **No targetGraphic** – Unity Button needs `targetGraphic` set and that Graphic must have `raycastTarget = true` to receive events.
4. **Canvas/manager disabled** – If the CMD quick-guide canvas is the same GameObject as its manager, disabling the canvas in Awake stops the Show coroutine from running.

## Changes made (line-by-line)

### PaintTab_LayersPanel_UI.cs

| Line / area | Purpose |
|-------------|--------|
| **Row Image** | `rowImg.raycastTarget = false` so the row never blocks child buttons. |
| **Row Button** | `Destroy(rowSelectBtn)` so the row has no Button and cannot intercept. |
| **Select button** | `selectImg.raycastTarget = true`, `selectBtn.targetGraphic = selectImg`, `selectBtn.interactable = true`. Shared `DoSelect()` used for both `onClick` and `LayerRowPointerDownForward.onPointerDown` so Select works on pointer down even if click is stolen. |
| **Delete button** | `delImg.raycastTarget = true`, `deleteBtn.targetGraphic = delImg`. `onClick` calls `TryRemoveLayer`. `LayerRowDeleteTrigger` implements both `IPointerClickHandler` and `IPointerDownHandler` and calls `TryRemoveLayer` so Delete works on pointer down or click. |
| **TryRemoveLayer** | Single entry point with 0.2s cooldown to avoid double delete when both Button and trigger fire. |
| **Visibility button** | Same as Select: `visImg.raycastTarget = true`, `visBtn.targetGraphic = visImg`, shared `DoVisibility()` for `onClick` and `LayerRowPointerDownForward.onPointerDown`. |
| **Name area** | Name has its own Button and raycast; does not cover the left-side buttons (layout order: Drag, Select, Delete, Visibility, Opacity, Name). |

### WelcomeScreenCMD_MGR.cs (black box quick guide)

| Line / area | Purpose |
|-------------|--------|
| **Awake** | Only call `_canvas.gameObject.SetActive(false)` when `_canvas.gameObject != gameObject`. If the Canvas is on the same GameObject as the manager, disabling it would stop the Show coroutine and the quick guide would never appear on EXE launch. |

## Cross-correlation fixes (nothing blocking implementation)

| Area | Risk | Fix |
|------|------|-----|
| **CMD panel** | **DisablePanel_ifDontShowOnStartup()** could set `_canvas.gameObject.SetActive(false)` when the canvas is the same GameObject as the manager → manager disabled, future Show() never runs. | Only call `SetActive(false)` when `_canvas.gameObject != gameObject` in both **Awake** and **DisablePanel_ifDontShowOnStartup**. |
| **Black box (terminal)** | **Launch_WebUI_bat_File** and **RestartTheWebui** called **Run_Bat_or_Shortcut_or_Command** without **keepWindow** → default `false` → `cmd.exe /C` → window closes when the batch exits. | Pass **keepWindow: true** so the CMD/PowerShell window stays open (black box visible) while Stable Diffusion runs. |
| **Layer Select button** | Template or prefab rows might use a different child name; **Find("SelectLayer")** could return null and the blue button would never be wired. | Add the same fallback as Delete: iterate row children by name (case-insensitive) to find **SelectLayer**. |

## Build requirement for CMD quick guide

The CMD quick guide scene must be in **Build Settings** (File → Build Settings → Scenes in Build). Its path is:

`Assets/_gm/Features/Intro Panels/UI_MainWelcomeScreenCMD.unity`

If it is not in the build, `Start_Scene_Global_MGR` will skip it and log:  
`Skipping scene '...' because it is not in the Build Settings.`

See also **Assets/_gm/SOURCE_CODE_STRUCTURE_QUICK_REF.md** for the full black box / WebUI launch flow and source layout.
